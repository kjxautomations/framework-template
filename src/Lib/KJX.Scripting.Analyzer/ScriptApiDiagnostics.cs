using Microsoft.CodeAnalysis;

namespace KJX.Scripting.Analyzer;

/// <summary>
/// The complete set of rules that keep the source generator total: if a script API interface
/// compiles, the generator can emit dispatch, a descriptor and client proxies for it without
/// any remaining "what if" cases.
/// </summary>
internal static class ScriptApiDiagnostics
{
    private const string Category = "KJX.Scripting";

    private const string SupportedTypes =
        "Supported types are the primitives, string, decimal, DateTimeOffset, TimeSpan, Guid, enums, " +
        "record and record struct DTOs composed of those, single-dimension arrays and IReadOnlyList<T> " +
        "of those, nullable forms of any of them, and interfaces marked [ScriptApi] at parameter or " +
        "return position. A trailing CancellationToken parameter is allowed and is not exposed to scripts.";

    internal static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "KJXSA001",
        title: "Unsupported type on the script API surface",
        messageFormat: "Member '{0}' of script API interface '{1}' uses type '{2}', which cannot cross the script boundary",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: SupportedTypes);

    internal static readonly DiagnosticDescriptor EventNotSupported = new(
        id: "KJXSA002",
        title: "Events are not scriptable",
        messageFormat: "Event '{0}' is part of the surface of script API interface '{1}'; events cannot cross the script boundary",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Expose an IAsyncEnumerable<T> member instead, which becomes a subscription on the wire. " +
                     "If the event is for in-process consumers only, move it to a separate interface that is not marked [ScriptApi].");

    internal static readonly DiagnosticDescriptor GenericNotSupported = new(
        id: "KJXSA003",
        title: "Generic declarations are not scriptable",
        messageFormat: "{0} '{1}' is generic; the script API dispatches by name and has no way to supply type arguments",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Declare a concrete, non-generic member. Generic interfaces cannot be marked [ScriptApi] at all.");

    internal static readonly DiagnosticDescriptor ByRefNotSupported = new(
        id: "KJXSA004",
        title: "ref, out and in are not scriptable",
        messageFormat: "{0} of member '{1}' on script API interface '{2}' is passed by reference; the wire protocol has no by-reference form",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Return a record DTO instead of using out or ref parameters.");

    internal static readonly DiagnosticDescriptor DuplicateMemberName = new(
        id: "KJXSA005",
        title: "Script API member names must be unique",
        messageFormat: "Script API interface '{0}' produces the wire name '{1}' more than once, from '{2}' and '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Wire dispatch is by name only, so overloads are not permitted and no two members may " +
                     "reduce to the same snake_case name. Note that a trailing Async is stripped and that " +
                     "properties produce get_ and set_ names.");

    internal static readonly DiagnosticDescriptor ReferenceInsideDto = new(
        id: "KJXSA006",
        title: "Object references are not permitted inside DTOs",
        messageFormat: "Member '{0}' of script API interface '{1}' uses DTO '{2}', whose property '{3}' is the script API interface '{4}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Object references are permitted only at parameter and return position, including inside " +
                     "arrays and lists there. A reference buried in a DTO cannot be validated against the " +
                     "declared parameter type on arrival.");

    internal static readonly DiagnosticDescriptor DelegateNotSupported = new(
        id: "KJXSA007",
        title: "Delegates are not scriptable",
        messageFormat: "Member '{0}' of script API interface '{1}' uses delegate type '{2}'; a script cannot be handed a callback",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Expose an IAsyncEnumerable<T> member instead, which becomes a subscription on the wire. " +
                     "If the callback is for in-process consumers only, move the member to a separate interface " +
                     "that is not marked [ScriptApi].");

    internal static readonly DiagnosticDescriptor UnsupportedMember = new(
        id: "KJXSA008",
        title: "Unsupported member kind on a script API interface",
        messageFormat: "{0} on script API interface '{1}' has no script API form",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Indexers and static members cannot be addressed by the '<target>.<member>' wire form. " +
                     "Use an ordinary method or property, or move the member to an interface that is not marked [ScriptApi].");
}
