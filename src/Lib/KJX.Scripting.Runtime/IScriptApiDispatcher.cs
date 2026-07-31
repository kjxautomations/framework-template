using System.Text.Json;
using System.Text.Json.Nodes;

namespace KJX.Scripting.Runtime;

/// <summary>How a member is invoked on the wire.</summary>
public enum ScriptApiMemberKind
{
    /// <summary>A request/response call.</summary>
    Call,

    /// <summary>A subscription that produces notifications until it is cancelled.</summary>
    Stream,
}

/// <summary>
/// Turns the two reference forms on the wire into live objects and back. Implemented by the RPC
/// host, which owns the device registry and the per-session handle table; the generated dispatch
/// never resolves a reference itself.
/// </summary>
public interface IScriptApiReferences
{
    /// <summary>
    /// Resolves a <c>{"$ref": "...", "$type": "..."}</c> envelope. The <c>$type</c> it carries is
    /// advisory: implementations must validate against <paramref name="expectedWireTypeName"/> and
    /// their own table.
    /// </summary>
    /// <param name="reference">The envelope as it arrived.</param>
    /// <param name="expectedWireTypeName">The wire type the member declares for this position.</param>
    /// <param name="parameterName">The parameter being bound, for error reporting.</param>
    object Resolve(JsonElement reference, string expectedWireTypeName, string parameterName);

    /// <summary>
    /// Produces the envelope for an object leaving the boundary, minting a handle if the object
    /// is not already addressable.
    /// </summary>
    /// <param name="target">The object being returned. May be null.</param>
    /// <param name="declaredWireTypeName">The wire type the member declares for this position.</param>
    JsonNode Describe(object target, string declaredWireTypeName);
}

/// <summary>
/// The generated dispatch table for one <c>[ScriptApi]</c> interface: a switch over member names
/// with typed argument extraction. There is no reflection over parameters anywhere behind this
/// interface, which is what keeps the call path trim-safe.
/// </summary>
public interface IScriptApiDispatcher
{
    /// <summary>The name this interface is known by on the wire.</summary>
    string WireTypeName { get; }

    /// <summary>The interface this dispatcher invokes.</summary>
    Type TargetType { get; }

    /// <summary>
    /// The wire type names this one inherits from. Members of those types are dispatched by their
    /// own dispatchers rather than being repeated here.
    /// </summary>
    IReadOnlyList<string> Extends { get; }

    /// <summary>The members declared directly by this interface, as wire names.</summary>
    IReadOnlyList<string> MemberNames { get; }

    /// <summary>Looks up how a member is invoked, without invoking it.</summary>
    bool TryGetMemberKind(string member, out ScriptApiMemberKind kind);

    /// <summary>
    /// Invokes a call. Arguments arrive as a by-name object; the result is the JSON value to
    /// return, or null for members that produce nothing.
    /// </summary>
    ValueTask<JsonNode> InvokeAsync(
        object target,
        string member,
        JsonElement arguments,
        IScriptApiReferences references,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a subscription. Enumeration ends when the source completes or
    /// <paramref name="cancellationToken"/> fires.
    /// </summary>
    IAsyncEnumerable<JsonNode> Subscribe(
        object target,
        string member,
        JsonElement arguments,
        IScriptApiReferences references,
        CancellationToken cancellationToken);
}
