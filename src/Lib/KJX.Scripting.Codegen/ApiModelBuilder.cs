using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KJX.Scripting.Shared;
using Microsoft.CodeAnalysis;

namespace KJX.Scripting.Codegen;

/// <summary>
/// Turns the marked interfaces of a compilation into the model that both the descriptor and the
/// dispatch tables are written from. The two outputs cannot disagree because they are produced
/// from this one description of the surface.
/// </summary>
internal sealed class ApiModelBuilder
{
    private static readonly SymbolDisplayFormat ClrFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly ScriptApiTypeModel _types;
    private readonly Action<Diagnostic> _report;

    private readonly Dictionary<INamedTypeSymbol, ApiDto> _dtos = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ApiEnum> _enums = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, ISymbol> _claimedNames = new(StringComparer.Ordinal);
    private readonly Queue<INamedTypeSymbol> _pendingDtos = new();

    private ApiModelBuilder(ScriptApiTypeModel types, Action<Diagnostic> report)
    {
        _types = types;
        _report = report;
    }

    /// <summary>Builds the model, or returns null when the compilation has no scripting surface.</summary>
    public static ApiModel Build(
        Compilation compilation,
        IReadOnlyList<INamedTypeSymbol> interfaces,
        string generatedNamespace,
        Action<Diagnostic> report)
    {
        var types = ScriptApiTypeModel.TryCreate(compilation);
        if (types == null || interfaces.Count == 0)
            return null;

        return new ApiModelBuilder(types, report).BuildCore(interfaces, generatedNamespace);
    }

    private ApiModel BuildCore(IReadOnlyList<INamedTypeSymbol> interfaces, string generatedNamespace)
    {
        var model = new ApiModel { GeneratedNamespace = generatedNamespace };

        var ordered = interfaces
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .OrderBy(symbol => symbol.ToDisplayString(ClrFormat), StringComparer.Ordinal)
            .ToList();

        foreach (var symbol in ordered)
        {
            var name = _types.GetWireTypeName(symbol);
            if (!Claim(name, symbol))
                continue;

            model.Types.Add(BuildType(symbol, name));
        }

        // DTOs discovered while walking signatures can themselves reference further DTOs.
        while (_pendingDtos.Count > 0)
            CompleteDto(_pendingDtos.Dequeue());

        model.Dtos.AddRange(_dtos.Values.OrderBy(dto => dto.Name, StringComparer.Ordinal));
        model.Enums.AddRange(_enums.Values.OrderBy(item => item.Name, StringComparer.Ordinal));
        model.Types.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        return model;
    }

    private bool Claim(string name, ISymbol symbol)
    {
        if (_claimedNames.TryGetValue(name, out var existing))
        {
            if (!SymbolEqualityComparer.Default.Equals(existing, symbol))
            {
                _report(Diagnostic.Create(
                    CodegenDiagnostics.DuplicateWireName,
                    Location(symbol),
                    existing.ToDisplayString(ClrFormat),
                    symbol.ToDisplayString(ClrFormat),
                    name));
            }

            return false;
        }

        _claimedNames.Add(name, symbol);
        return true;
    }

    private ApiType BuildType(INamedTypeSymbol symbol, string wireName)
    {
        var type = new ApiType
        {
            Name = wireName,
            Clr = symbol.ToDisplayString(ClrFormat),
            Doc = DocumentationReader.Summary(symbol),
            DispatcherName = Identifier(wireName) + "Dispatcher",
        };

        foreach (var baseInterface in symbol.Interfaces.Where(_types.IsScriptApiInterface))
            type.Extends.Add(_types.GetWireTypeName(baseInterface));

        type.Extends.Sort(StringComparer.Ordinal);

        foreach (var member in _types.GetScriptableMembers(symbol))
        {
            switch (member)
            {
                case IPropertySymbol property:
                    BuildProperty(type, property);
                    break;

                case IMethodSymbol method:
                    BuildMethod(type, method);
                    break;
            }
        }

        type.Members.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return type;
    }

    private void BuildProperty(ApiType type, IPropertySymbol property)
    {
        if (property.IsIndexer || property.IsStatic)
            return;

        if (_types.TryGetStreamElement(property.Type, out var element))
        {
            var stream = new ApiMember
            {
                Name = ScriptApiNaming.MethodName(property.Name),
                ClrName = property.Name,
                Kind = ApiMemberKind.Stream,
                Doc = DocumentationReader.Summary(property),
                Returns = ResolveOrSkip(element, type, property),
                FromProperty = true,
            };

            if (stream.Returns != null)
                type.Members.Add(stream);

            return;
        }

        var valueType = ResolveOrSkip(property.Type, type, property);
        if (valueType == null)
            return;

        type.Members.Add(new ApiMember
        {
            Name = ScriptApiNaming.PropertyGetterName(property.Name),
            ClrName = property.Name,
            Kind = ApiMemberKind.Get,
            Doc = DocumentationReader.Summary(property),
            Returns = valueType,
            FromProperty = true,
        });

        if (property.SetMethod == null ||
            property.SetMethod.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod.IsInitOnly)
        {
            return;
        }

        var setter = new ApiMember
        {
            Name = ScriptApiNaming.PropertySetterName(property.Name),
            ClrName = property.Name,
            Kind = ApiMemberKind.Set,
            Doc = DocumentationReader.Summary(property),
            FromProperty = true,
        };

        setter.Parameters.Add(new ApiParameter
        {
            Name = "value",
            ClrName = "value",
            Type = valueType,
        });

        type.Members.Add(setter);

        // Reading a DTO argument needs a constructor; the getter alone does not.
        RequireReadable(valueType, type, property);
    }

    private void BuildMethod(ApiType type, IMethodSymbol method)
    {
        if (method.IsStatic || method.TypeParameters.Length > 0 || method.MethodKind != MethodKind.Ordinary)
            return;

        var member = new ApiMember
        {
            Name = ScriptApiNaming.MethodName(method.Name),
            ClrName = method.Name,
            Kind = ApiMemberKind.Call,
            Doc = DocumentationReader.Summary(method),
        };

        var returnType = method.ReturnType;

        if (_types.TryGetAwaitedResult(returnType, out var awaited))
        {
            member.IsAwaitable = true;
            returnType = awaited;
        }

        if (returnType != null && _types.TryGetStreamElement(returnType, out var element))
        {
            member.Kind = ApiMemberKind.Stream;
            returnType = element;
        }

        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            member.Returns = ResolveOrSkip(returnType, type, method);
            if (member.Returns == null)
                return;
        }

        var documented = DocumentationReader.Parameters(method);

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];

            if (parameter.RefKind != RefKind.None)
                return;

            if (_types.IsCancellationToken(parameter.Type))
            {
                if (i == method.Parameters.Length - 1)
                {
                    member.PassesCancellationToken = true;
                    continue;
                }

                return;
            }

            var parameterType = ResolveOrSkip(parameter.Type, type, method);
            if (parameterType == null)
                return;

            RequireReadable(parameterType, type, method);

            var modelled = new ApiParameter
            {
                Name = ScriptApiNaming.ParameterName(parameter.Name),
                ClrName = parameter.Name,
                Type = parameterType,
                Doc = documented.TryGetValue(parameter.Name, out var doc) ? doc : null,
            };

            ApplyDefault(parameter, modelled);
            member.Parameters.Add(modelled);
        }

        type.Members.Add(member);
    }

    private ApiTypeRef ResolveOrSkip(ITypeSymbol type, ApiType owner, ISymbol member)
    {
        var resolved = Resolve(type);
        if (resolved != null)
            return resolved;

        _report(Diagnostic.Create(
            CodegenDiagnostics.MemberSkipped,
            Location(member),
            member.Name,
            owner.Name,
            $"'{type.ToDisplayString(ClrFormat)}' has no script API form"));

        return null;
    }

    private ApiTypeRef Resolve(ITypeSymbol type)
    {
        if (type == null)
            return null;

        if (ScriptApiTypeModel.TryGetNullableValue(type, out var underlying))
        {
            var inner = Resolve(underlying);
            if (inner == null)
                return null;

            inner.Nullable = true;
            inner.Clr = type.ToDisplayString(ClrFormat);
            return inner;
        }

        if (type is IArrayTypeSymbol array && array.Rank == 1)
        {
            var items = Resolve(array.ElementType);
            return items == null
                ? null
                : new ApiTypeRef
                {
                    Kind = ApiTypeKinds.Array,
                    Items = items,
                    Clr = type.ToDisplayString(ClrFormat),
                    Nullable = type.NullableAnnotation == NullableAnnotation.Annotated,
                    IsValueType = false,
                };
        }

        if (_types.TryGetListElement(type, out var element))
        {
            var items = Resolve(element);
            return items == null
                ? null
                : new ApiTypeRef
                {
                    Kind = ApiTypeKinds.List,
                    Items = items,
                    Clr = type.ToDisplayString(ClrFormat),
                    Nullable = type.NullableAnnotation == NullableAnnotation.Annotated,
                    IsValueType = false,
                };
        }

        var scalar = ScalarKind(type);
        if (scalar != null)
        {
            return new ApiTypeRef
            {
                Kind = scalar,
                Clr = type.ToDisplayString(ClrFormat),
                Nullable = type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated,
                IsValueType = type.IsValueType,
            };
        }

        if (type is not INamedTypeSymbol named)
            return null;

        if (named.TypeKind == TypeKind.Enum)
        {
            var declared = RegisterEnum(named);
            return declared == null
                ? null
                : new ApiTypeRef
                {
                    Kind = ApiTypeKinds.Enum,
                    Name = declared.Name,
                    Clr = declared.Clr,
                    IsValueType = true,
                };
        }

        if (_types.IsScriptApiInterface(named))
        {
            return new ApiTypeRef
            {
                Kind = ApiTypeKinds.Reference,
                Name = _types.GetWireTypeName(named),
                Clr = named.ToDisplayString(ClrFormat),
                Nullable = named.NullableAnnotation == NullableAnnotation.Annotated,
                IsValueType = false,
            };
        }

        if (named.IsRecord && !named.IsGenericType)
        {
            var declared = RegisterDto(named);
            return declared == null
                ? null
                : new ApiTypeRef
                {
                    Kind = ApiTypeKinds.Dto,
                    Name = declared.Name,
                    Clr = declared.Clr,
                    Nullable = named.IsReferenceType && named.NullableAnnotation == NullableAnnotation.Annotated,
                    IsValueType = named.IsValueType,
                };
        }

        return null;
    }

    private static string ScalarKind(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return ApiTypeKinds.Boolean;
            case SpecialType.System_SByte: return ApiTypeKinds.SByte;
            case SpecialType.System_Byte: return ApiTypeKinds.Byte;
            case SpecialType.System_Int16: return ApiTypeKinds.Int16;
            case SpecialType.System_UInt16: return ApiTypeKinds.UInt16;
            case SpecialType.System_Int32: return ApiTypeKinds.Int32;
            case SpecialType.System_UInt32: return ApiTypeKinds.UInt32;
            case SpecialType.System_Int64: return ApiTypeKinds.Int64;
            case SpecialType.System_UInt64: return ApiTypeKinds.UInt64;
            case SpecialType.System_Single: return ApiTypeKinds.Single;
            case SpecialType.System_Double: return ApiTypeKinds.Double;
            case SpecialType.System_Decimal: return ApiTypeKinds.Decimal;
            case SpecialType.System_Char: return ApiTypeKinds.Char;
            case SpecialType.System_String: return ApiTypeKinds.String;
        }

        return type.ToDisplayString(ClrFormat) switch
        {
            "global::System.Guid" => ApiTypeKinds.Guid,
            "global::System.DateTimeOffset" => ApiTypeKinds.DateTimeOffset,
            "global::System.TimeSpan" => ApiTypeKinds.TimeSpan,
            _ => null,
        };
    }

    private ApiEnum RegisterEnum(INamedTypeSymbol symbol)
    {
        if (_enums.TryGetValue(symbol, out var existing))
            return existing;

        var name = ScriptApiNaming.ToSnakeCase(symbol.Name);
        if (!Claim(name, symbol))
            return null;

        var declared = new ApiEnum
        {
            Name = name,
            Clr = symbol.ToDisplayString(ClrFormat),
            Doc = DocumentationReader.Summary(symbol),
        };

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
        {
            declared.Values.Add(new ApiEnumValue
            {
                Name = field.Name,
                ClrName = field.Name,
                Value = Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture),
            });
        }

        _enums.Add(symbol, declared);
        return declared;
    }

    private ApiDto RegisterDto(INamedTypeSymbol symbol)
    {
        if (_dtos.TryGetValue(symbol, out var existing))
            return existing;

        var name = ScriptApiNaming.ToSnakeCase(symbol.Name);
        if (!Claim(name, symbol))
            return null;

        var declared = new ApiDto
        {
            Name = name,
            Clr = symbol.ToDisplayString(ClrFormat),
            Doc = DocumentationReader.Summary(symbol),
        };

        // Registered before its properties are walked so that a DTO containing itself terminates.
        _dtos.Add(symbol, declared);
        _pendingDtos.Enqueue(symbol);
        return declared;
    }

    private void CompleteDto(INamedTypeSymbol symbol)
    {
        var declared = _dtos[symbol];

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer ||
                property.DeclaredAccessibility != Accessibility.Public ||
                property.GetMethod == null)
            {
                continue;
            }

            var type = Resolve(property.Type);
            if (type == null)
                continue;

            declared.Properties.Add(new ApiDtoProperty
            {
                Name = ScriptApiNaming.ToSnakeCase(property.Name),
                ClrName = property.Name,
                Type = type,
                Doc = DocumentationReader.Summary(property),
                IsSettable = property.SetMethod != null &&
                             property.SetMethod.DeclaredAccessibility == Accessibility.Public,
            });
        }

        ChooseConstructor(symbol, declared);
    }

    /// <summary>
    /// Works out how the DTO is rebuilt: a constructor whose parameters line up with properties,
    /// with anything left over assigned in an object initializer.
    /// </summary>
    private static void ChooseConstructor(INamedTypeSymbol symbol, ApiDto declared)
    {
        var byName = declared.Properties.ToDictionary(p => p.ClrName, StringComparer.OrdinalIgnoreCase);

        var best = symbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)
            .Where(c => c.Parameters.All(p => byName.ContainsKey(p.Name)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (best == null)
        {
            declared.IsReadable = false;
            return;
        }

        foreach (var parameter in best.Parameters)
            declared.ConstructorProperties.Add(byName[parameter.Name]);

        var covered = new HashSet<string>(
            declared.ConstructorProperties.Select(p => p.ClrName),
            StringComparer.Ordinal);

        declared.IsReadable = declared.Properties
            .Where(property => !covered.Contains(property.ClrName))
            .All(property => property.IsSettable);
    }

    /// <summary>
    /// Checks that everything reachable from an incoming argument can actually be rebuilt. Only
    /// arguments need this: a DTO that is only ever returned just has its properties read.
    /// </summary>
    private void RequireReadable(ApiTypeRef type, ApiType owner, ISymbol member)
    {
        switch (type?.Kind)
        {
            case ApiTypeKinds.Array:
            case ApiTypeKinds.List:
                RequireReadable(type.Items, owner, member);
                return;

            case ApiTypeKinds.Dto:
                var dto = _dtos.Values.FirstOrDefault(candidate => candidate.Name == type.Name);
                if (dto == null)
                    return;

                // The DTO may not have been walked yet; drain the queue so IsReadable is known.
                while (_pendingDtos.Count > 0)
                    CompleteDto(_pendingDtos.Dequeue());

                if (!dto.IsReadable)
                {
                    _report(Diagnostic.Create(
                        CodegenDiagnostics.DtoNotConstructible,
                        Location(member),
                        dto.Clr.Replace("global::", string.Empty),
                        $"{owner.Name}.{ScriptApiNaming.MethodName(member.Name)}",
                        "no public constructor or settable property covers every property"));
                }

                foreach (var property in dto.Properties)
                    RequireReadable(property.Type, owner, member);

                return;
        }
    }

    private static void ApplyDefault(IParameterSymbol parameter, ApiParameter modelled)
    {
        if (!parameter.HasExplicitDefaultValue)
            return;

        modelled.HasDefault = true;

        var value = parameter.ExplicitDefaultValue;
        var type = parameter.Type;

        if (value == null)
        {
            modelled.DefaultJson = "null";
            modelled.DefaultExpression = type.IsValueType
                ? $"default({type.ToDisplayString(ClrFormat)})"
                : "null";
            return;
        }

        var underlying = ScriptApiTypeModel.TryGetNullableValue(type, out var nullable) ? nullable : type;

        if (underlying.TypeKind == TypeKind.Enum && underlying is INamedTypeSymbol enumType)
        {
            var field = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));

            modelled.DefaultJson = field == null
                ? "null"
                : JsonText.Quote(field.Name);
            modelled.DefaultExpression = field == null
                ? $"({enumType.ToDisplayString(ClrFormat)})({Convert.ToString(value, CultureInfo.InvariantCulture)})"
                : $"{enumType.ToDisplayString(ClrFormat)}.{field.Name}";
            return;
        }

        switch (value)
        {
            case bool flag:
                modelled.DefaultJson = flag ? "true" : "false";
                modelled.DefaultExpression = flag ? "true" : "false";
                return;

            case string text:
                modelled.DefaultJson = JsonText.Quote(text);
                modelled.DefaultExpression = JsonText.CSharpLiteral(text);
                return;

            case char character:
                modelled.DefaultJson = JsonText.Quote(character.ToString());
                modelled.DefaultExpression = JsonText.CSharpCharLiteral(character);
                return;

            case double number:
                modelled.DefaultJson = Number(number);
                modelled.DefaultExpression = Number(number) + "D";
                return;

            case float number:
                modelled.DefaultJson = Number(number);
                modelled.DefaultExpression = Number(number) + "F";
                return;

            case decimal number:
                modelled.DefaultJson = number.ToString(CultureInfo.InvariantCulture);
                modelled.DefaultExpression = number.ToString(CultureInfo.InvariantCulture) + "M";
                return;

            case long number:
                modelled.DefaultJson = number.ToString(CultureInfo.InvariantCulture);
                modelled.DefaultExpression = number.ToString(CultureInfo.InvariantCulture) + "L";
                return;

            case ulong number:
                modelled.DefaultJson = number.ToString(CultureInfo.InvariantCulture);
                modelled.DefaultExpression = number.ToString(CultureInfo.InvariantCulture) + "UL";
                return;

            case uint number:
                modelled.DefaultJson = number.ToString(CultureInfo.InvariantCulture);
                modelled.DefaultExpression = number.ToString(CultureInfo.InvariantCulture) + "U";
                return;

            default:
                var literal = Convert.ToString(value, CultureInfo.InvariantCulture);
                modelled.DefaultJson = literal;
                modelled.DefaultExpression = $"({underlying.ToDisplayString(ClrFormat)})({literal})";
                return;
        }
    }

    private static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>snake_case back to an identifier, for naming generated classes.</summary>
    private static string Identifier(string wireName)
    {
        var parts = wireName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part.Substring(1) : string.Empty)));
    }

    private static Location Location(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Microsoft.CodeAnalysis.Location.None;
}
