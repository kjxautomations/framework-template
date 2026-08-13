using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using KJX.Scripting.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KJX.Scripting.Analyzer;

/// <summary>
/// Rejects anything on a <c>[ScriptApi]</c> interface that the source generator could not turn
/// into dispatch, a descriptor entry and a client proxy. Everything the generator emits assumes
/// these rules held at compile time, which is why they are errors rather than warnings.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScriptApiAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            ScriptApiDiagnostics.UnsupportedType,
            ScriptApiDiagnostics.EventNotSupported,
            ScriptApiDiagnostics.GenericNotSupported,
            ScriptApiDiagnostics.ByRefNotSupported,
            ScriptApiDiagnostics.DuplicateMemberName,
            ScriptApiDiagnostics.ReferenceInsideDto,
            ScriptApiDiagnostics.DelegateNotSupported,
            ScriptApiDiagnostics.UnsupportedMember);

    private static readonly SymbolDisplayFormat MemberFormat = SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var model = ScriptApiTypeModel.TryCreate(start.Compilation);
            if (model == null)
                return;

            start.RegisterSymbolAction(symbolContext => Analyze(symbolContext, model), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ScriptApiTypeModel model)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Interface || !model.IsScriptApiInterface(type))
            return;

        if (type.IsGenericType)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ScriptApiDiagnostics.GenericNotSupported, DeclarationLocation(type), "Interface", type.Name));
        }

        var wireNames = new Dictionary<string, ISymbol>();

        foreach (var member in model.GetScriptableMembers(type))
        {
            switch (member)
            {
                case IEventSymbol eventSymbol:
                    Report(context, type, eventSymbol, ScriptApiDiagnostics.EventNotSupported,
                        eventSymbol.Name, type.Name);
                    break;

                case IPropertySymbol property:
                    AnalyzeProperty(context, model, type, property, wireNames);
                    break;

                case IMethodSymbol method:
                    AnalyzeMethod(context, model, type, method, wireNames);
                    break;
            }
        }
    }

    private static void AnalyzeProperty(
        SymbolAnalysisContext context,
        ScriptApiTypeModel model,
        INamedTypeSymbol type,
        IPropertySymbol property,
        Dictionary<string, ISymbol> wireNames)
    {
        if (property.IsIndexer)
        {
            Report(context, type, property, ScriptApiDiagnostics.UnsupportedMember, "Indexer", type.Name);
            return;
        }

        if (property.IsStatic)
        {
            Report(context, type, property, ScriptApiDiagnostics.UnsupportedMember,
                $"Static property '{property.Name}'", type.Name);
            return;
        }

        ReportTypeValidation(context, model, type, property, model.ValidateReturnType(property.Type), property.Type);

        // An IAsyncEnumerable<T> property is a subscription, not a getter.
        if (model.IsStream(property.Type))
        {
            ClaimWireName(context, type, property, ScriptApiNaming.MethodName(property.Name), wireNames);
            return;
        }

        ClaimWireName(context, type, property, ScriptApiNaming.PropertyGetterName(property.Name), wireNames);

        if (property.SetMethod != null &&
            property.SetMethod.DeclaredAccessibility == Accessibility.Public &&
            !property.SetMethod.IsInitOnly)
        {
            ClaimWireName(context, type, property, ScriptApiNaming.PropertySetterName(property.Name), wireNames);
        }
    }

    private static void AnalyzeMethod(
        SymbolAnalysisContext context,
        ScriptApiTypeModel model,
        INamedTypeSymbol type,
        IMethodSymbol method,
        Dictionary<string, ISymbol> wireNames)
    {
        if (method.IsStatic)
        {
            Report(context, type, method, ScriptApiDiagnostics.UnsupportedMember,
                $"Static method '{method.Name}'", type.Name);
            return;
        }

        if (method.TypeParameters.Length > 0)
        {
            Report(context, type, method, ScriptApiDiagnostics.GenericNotSupported, "Method", method.Name);
            return;
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            Report(context, type, method, ScriptApiDiagnostics.ByRefNotSupported,
                "Return value", method.Name, type.Name);
        }

        ReportTypeValidation(context, model, type, method, model.ValidateReturnType(method.ReturnType), method.ReturnType);

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];

            if (parameter.RefKind != RefKind.None)
            {
                Report(context, type, method, ScriptApiDiagnostics.ByRefNotSupported,
                    $"Parameter '{parameter.Name}'", method.Name, type.Name);
                continue;
            }

            // A trailing CancellationToken is supplied by the host and never reaches the script.
            if (model.IsCancellationToken(parameter.Type))
            {
                if (i != method.Parameters.Length - 1)
                {
                    Report(context, type, method, ScriptApiDiagnostics.UnsupportedType,
                        method.Name, type.Name, parameter.Type.ToDisplayString());
                }

                continue;
            }

            ReportTypeValidation(context, model, type, method, model.ValidateParameterType(parameter.Type), parameter.Type);
        }

        ClaimWireName(context, type, method, ScriptApiNaming.MethodName(method.Name), wireNames);
    }

    private static void ReportTypeValidation(
        SymbolAnalysisContext context,
        ScriptApiTypeModel model,
        INamedTypeSymbol type,
        ISymbol member,
        TypeValidation validation,
        ITypeSymbol declaredType)
    {
        switch (validation.Kind)
        {
            case TypeValidationKind.Ok:
                return;

            case TypeValidationKind.Delegate:
                Report(context, type, member, ScriptApiDiagnostics.DelegateNotSupported,
                    member.Name, type.Name, validation.Offender.ToDisplayString());
                return;

            case TypeValidationKind.ReferenceInsideDto:
                Report(context, type, member, ScriptApiDiagnostics.ReferenceInsideDto,
                    member.Name,
                    type.Name,
                    (validation.Dto ?? (ISymbol)declaredType).Name,
                    validation.DtoProperty?.Name ?? "?",
                    validation.Offender.ToDisplayString());
                return;

            default:
                Report(context, type, member, ScriptApiDiagnostics.UnsupportedType,
                    member.Name, type.Name, validation.Offender.ToDisplayString());
                return;
        }
    }

    private static void ClaimWireName(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        ISymbol member,
        string wireName,
        Dictionary<string, ISymbol> wireNames)
    {
        if (wireNames.TryGetValue(wireName, out var existing))
        {
            if (!SymbolEqualityComparer.Default.Equals(existing, member))
            {
                Report(context, type, member, ScriptApiDiagnostics.DuplicateMemberName,
                    type.Name,
                    wireName,
                    existing.ToDisplayString(MemberFormat),
                    member.ToDisplayString(MemberFormat));
            }

            return;
        }

        wireNames.Add(wireName, member);
    }

    private static void Report(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        ISymbol member,
        DiagnosticDescriptor descriptor,
        params object[] messageArgs)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, ReportLocation(type, member), messageArgs));
    }

    /// <summary>
    /// Inherited members are reported on the interface that pulled them in, which is the one the
    /// author can act on. Members written on the interface itself are reported in place.
    /// </summary>
    private static Location ReportLocation(INamedTypeSymbol type, ISymbol member)
    {
        if (SymbolEqualityComparer.Default.Equals(member.ContainingType, type))
        {
            var memberLocation = member.Locations.FirstOrDefault(location => location.IsInSource);
            if (memberLocation != null)
                return memberLocation;
        }

        return DeclarationLocation(type);
    }

    private static Location DeclarationLocation(INamedTypeSymbol type) =>
        type.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
}
