using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace KJX.Scripting.Analyzer;

/// <summary>How a type failed validation, or <see cref="TypeValidationKind.Ok"/>.</summary>
internal enum TypeValidationKind
{
    Ok,
    Unsupported,
    Delegate,
    ReferenceInsideDto,
}

/// <summary>The outcome of validating one type against the script API type set.</summary>
internal readonly struct TypeValidation
{
    private TypeValidation(TypeValidationKind kind, ITypeSymbol offender, INamedTypeSymbol dto, IPropertySymbol dtoProperty)
    {
        Kind = kind;
        Offender = offender;
        Dto = dto;
        DtoProperty = dtoProperty;
    }

    public TypeValidationKind Kind { get; }

    /// <summary>The type that broke the rule, which may be nested inside the declared type.</summary>
    public ITypeSymbol Offender { get; }

    /// <summary>For <see cref="TypeValidationKind.ReferenceInsideDto"/>, the DTO holding the reference.</summary>
    public INamedTypeSymbol Dto { get; }

    /// <summary>For <see cref="TypeValidationKind.ReferenceInsideDto"/>, the offending property.</summary>
    public IPropertySymbol DtoProperty { get; }

    public bool IsOk => Kind == TypeValidationKind.Ok;

    public static TypeValidation Ok() => new(TypeValidationKind.Ok, null, null, null);

    public static TypeValidation Unsupported(ITypeSymbol offender) =>
        new(TypeValidationKind.Unsupported, offender, null, null);

    public static TypeValidation Delegate(ITypeSymbol offender) =>
        new(TypeValidationKind.Delegate, offender, null, null);

    public static TypeValidation ReferenceInsideDto(INamedTypeSymbol dto, IPropertySymbol property, ITypeSymbol reference) =>
        new(TypeValidationKind.ReferenceInsideDto, reference, dto, property);
}

/// <summary>
/// Answers the two questions the analyzer and the generator both need: which members make up the
/// scriptable surface of an interface, and whether a given type is allowed to cross the boundary.
/// </summary>
internal sealed class ScriptApiTypeModel
{
    /// <summary>
    /// Interfaces whose members are infrastructure rather than API. Members declared on these are
    /// neither exported nor diagnosed, wherever they appear in an inheritance chain. The set is
    /// closed: an event you declare yourself is still an error.
    /// </summary>
    private static readonly string[] ExemptInterfaceMetadataNames =
    {
        "System.ComponentModel.INotifyPropertyChanged",
        "System.ComponentModel.INotifyPropertyChanging",
        "System.ComponentModel.INotifyDataErrorInfo",
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.IEquatable`1",
    };

    private static readonly SpecialType[] SupportedSpecialTypes =
    {
        SpecialType.System_Boolean,
        SpecialType.System_Char,
        SpecialType.System_SByte,
        SpecialType.System_Byte,
        SpecialType.System_Int16,
        SpecialType.System_UInt16,
        SpecialType.System_Int32,
        SpecialType.System_UInt32,
        SpecialType.System_Int64,
        SpecialType.System_UInt64,
        SpecialType.System_Single,
        SpecialType.System_Double,
        SpecialType.System_Decimal,
        SpecialType.System_String,
    };

    private readonly INamedTypeSymbol _scriptApiAttribute;
    private readonly HashSet<INamedTypeSymbol> _exemptInterfaces;
    private readonly HashSet<INamedTypeSymbol> _supportedNamedTypes;
    private readonly INamedTypeSymbol _readOnlyList;
    private readonly INamedTypeSymbol _asyncEnumerable;
    private readonly INamedTypeSymbol _task;
    private readonly INamedTypeSymbol _taskOfT;
    private readonly INamedTypeSymbol _valueTask;
    private readonly INamedTypeSymbol _valueTaskOfT;

    private ScriptApiTypeModel(Compilation compilation, INamedTypeSymbol scriptApiAttribute)
    {
        _scriptApiAttribute = scriptApiAttribute;

        _exemptInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var name in ExemptInterfaceMetadataNames)
        {
            var symbol = compilation.GetTypeByMetadataName(name);
            if (symbol != null)
                _exemptInterfaces.Add(symbol);
        }

        _supportedNamedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var name in new[] { "System.DateTimeOffset", "System.TimeSpan", "System.Guid" })
        {
            var symbol = compilation.GetTypeByMetadataName(name);
            if (symbol != null)
                _supportedNamedTypes.Add(symbol);
        }

        _readOnlyList = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1");
        _asyncEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        _task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        _taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        _valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        _valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        CancellationToken = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
    }

    /// <summary>The <c>CancellationToken</c> symbol, or null if the compilation has no reference to it.</summary>
    public INamedTypeSymbol CancellationToken { get; }

    /// <summary>
    /// Builds a model for the compilation, or returns null when <c>[ScriptApi]</c> is not
    /// referenced and there is therefore nothing to analyze.
    /// </summary>
    public static ScriptApiTypeModel TryCreate(Compilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName("KJX.Scripting.ScriptApiAttribute");
        return attribute == null ? null : new ScriptApiTypeModel(compilation, attribute);
    }

    /// <summary>True when the type is an interface carrying <c>[ScriptApi]</c>.</summary>
    public bool IsScriptApiInterface(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.TypeKind == TypeKind.Interface &&
        named.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _scriptApiAttribute));

    /// <summary>
    /// The members that make up the scriptable surface: everything declared on the interface plus
    /// everything inherited from unmarked, non-exempt base interfaces. Members of a base interface
    /// that is itself marked are left out; the descriptor records the inheritance instead.
    /// </summary>
    public IReadOnlyList<ISymbol> GetScriptableMembers(INamedTypeSymbol scriptApiInterface)
    {
        var members = new List<ISymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        Collect(scriptApiInterface, isRoot: true);
        return members;

        void Collect(INamedTypeSymbol type, bool isRoot)
        {
            if (!visited.Add(type))
                return;

            if (!isRoot && (IsScriptApiInterface(type) || IsExempt(type)))
                return;

            foreach (var member in type.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public)
                    continue;

                // Nested types are not part of the surface, and accessor methods are covered by
                // the property or event that owns them.
                if (member is INamedTypeSymbol)
                    continue;

                if (member is IMethodSymbol method && method.MethodKind != MethodKind.Ordinary)
                    continue;

                members.Add(member);
            }

            foreach (var baseInterface in type.Interfaces)
                Collect(baseInterface, isRoot: false);
        }
    }

    /// <summary>True for interfaces treated as infrastructure, such as INotifyPropertyChanged.</summary>
    public bool IsExempt(INamedTypeSymbol type) =>
        _exemptInterfaces.Contains(type.OriginalDefinition);

    /// <summary>
    /// Validates a return type, unwrapping <c>Task</c>, <c>ValueTask</c> and
    /// <c>IAsyncEnumerable&lt;T&gt;</c> first.
    /// </summary>
    public TypeValidation ValidateReturnType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
            return TypeValidation.Ok();

        if (Matches(type, _task) || Matches(type, _valueTask))
            return TypeValidation.Ok();

        if (type is INamedTypeSymbol named && named.IsGenericType &&
            (Matches(named.OriginalDefinition, _taskOfT) ||
             Matches(named.OriginalDefinition, _valueTaskOfT) ||
             Matches(named.OriginalDefinition, _asyncEnumerable)))
        {
            return ValidateValueType(named.TypeArguments[0], referencesAllowed: true);
        }

        return ValidateValueType(type, referencesAllowed: true);
    }

    /// <summary>Validates a parameter type. Object references are permitted here.</summary>
    public TypeValidation ValidateParameterType(ITypeSymbol type) =>
        ValidateValueType(type, referencesAllowed: true);

    /// <summary>True when the member is an <c>IAsyncEnumerable&lt;T&gt;</c>, i.e. a subscription.</summary>
    public bool IsStream(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.IsGenericType && Matches(named.OriginalDefinition, _asyncEnumerable);

    /// <summary>True for the trailing, implicit <c>CancellationToken</c> parameter.</summary>
    public bool IsCancellationToken(ITypeSymbol type) => Matches(type, CancellationToken);

    private TypeValidation ValidateValueType(ITypeSymbol type, bool referencesAllowed) =>
        ValidateValueType(type, referencesAllowed, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private TypeValidation ValidateValueType(ITypeSymbol type, bool referencesAllowed, HashSet<INamedTypeSymbol> visiting)
    {
        if (type == null || type.TypeKind == TypeKind.Error)
            return TypeValidation.Ok(); // the compiler is already reporting this one

        if (type.TypeKind == TypeKind.Delegate)
            return TypeValidation.Delegate(type);

        if (type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.Dynamic)
            return TypeValidation.Unsupported(type);

        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1
                ? ValidateValueType(array.ElementType, referencesAllowed, visiting)
                : TypeValidation.Unsupported(type);
        }

        if (!(type is INamedTypeSymbol named))
            return TypeValidation.Unsupported(type);

        // T? over a value type is the underlying type on the wire, plus null.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return ValidateValueType(named.TypeArguments[0], referencesAllowed, visiting);

        if (named.TypeKind == TypeKind.Enum)
            return TypeValidation.Ok();

        if (SupportedSpecialTypes.Contains(named.SpecialType) || _supportedNamedTypes.Contains(named))
            return TypeValidation.Ok();

        if (named.IsGenericType && Matches(named.OriginalDefinition, _readOnlyList))
            return ValidateValueType(named.TypeArguments[0], referencesAllowed, visiting);

        if (named.TypeKind == TypeKind.Interface)
        {
            if (!IsScriptApiInterface(named))
                return TypeValidation.Unsupported(named);

            return referencesAllowed
                ? TypeValidation.Ok()
                : TypeValidation.ReferenceInsideDto(null, null, named);
        }

        if (named.IsRecord && !named.IsGenericType)
            return ValidateDto(named, visiting);

        return TypeValidation.Unsupported(named);
    }

    private TypeValidation ValidateDto(INamedTypeSymbol dto, HashSet<INamedTypeSymbol> visiting)
    {
        // A DTO that contains itself is legal C#; guard the walk rather than the shape.
        if (!visiting.Add(dto))
            return TypeValidation.Ok();

        try
        {
            foreach (var property in dto.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public)
                    continue;
                if (property.GetMethod == null)
                    continue;

                var result = ValidateValueType(property.Type, referencesAllowed: false, visiting);
                if (result.IsOk)
                    continue;

                // Name the DTO and the property that carry the offending type, not just the type.
                return result.Kind == TypeValidationKind.ReferenceInsideDto
                    ? TypeValidation.ReferenceInsideDto(result.Dto ?? dto, result.DtoProperty ?? property, result.Offender)
                    : result;
            }

            return TypeValidation.Ok();
        }
        finally
        {
            visiting.Remove(dto);
        }
    }

    private static bool Matches(ITypeSymbol type, INamedTypeSymbol wellKnown) =>
        wellKnown != null && SymbolEqualityComparer.Default.Equals(type, wellKnown);
}
