using Microsoft.CodeAnalysis;

namespace KJX.Scripting.Codegen;

/// <summary>
/// The generator's own diagnostics. These cover the few things the analyzer cannot see from a
/// single interface: name collisions across the assembly, and DTOs that cannot be rebuilt from
/// their JSON form.
/// </summary>
internal static class CodegenDiagnostics
{
    private const string Category = "KJX.Scripting";

    internal static readonly DiagnosticDescriptor DuplicateWireName = new(
        id: "KJXSG001",
        title: "Script API names must be unique across the assembly",
        messageFormat: "'{0}' and '{1}' both use the wire name '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The wire name is how scripts address a type, so it has to identify one type. " +
                     "Pass an explicit name to [ScriptApi] on one of them, or rename it.");

    internal static readonly DiagnosticDescriptor DtoNotConstructible = new(
        id: "KJXSG002",
        title: "DTO cannot be rebuilt from its wire form",
        messageFormat: "'{0}' is used as an argument of '{1}' but cannot be constructed: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A DTO that arrives from a script has to be reconstructible from its properties, " +
                     "either through a constructor whose parameters match them or through settable properties.");

    internal static readonly DiagnosticDescriptor RuntimeNotReferenced = new(
        id: "KJXSG004",
        title: "Scripting runtime is not referenced",
        messageFormat: "This project declares [ScriptApi] interfaces but does not reference KJX.Scripting.Runtime, " +
                       "which the generated dispatch is written against",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Add a project or package reference to KJX.Scripting.Runtime.");

    internal static readonly DiagnosticDescriptor MemberSkipped = new(
        id: "KJXSG003",
        title: "Member left out of the script API",
        messageFormat: "'{0}' on '{1}' was left out of the generated dispatch: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The analyzer normally rejects these at the point of declaration. Seeing this warning " +
                     "means a KJXSA diagnostic was suppressed, and the member is absent from the script API.");
}
