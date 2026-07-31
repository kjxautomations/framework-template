using System.Text.Json.Nodes;

namespace KJX.Scripting.Runtime;

/// <summary>
/// The JSON-RPC error codes the scripting layer produces. The standard codes are used where they
/// fit; everything else lives in the implementation-defined server range.
/// </summary>
public static class ScriptApiErrorCodes
{
    /// <summary>The target exists but has no such member.</summary>
    public const int MemberNotFound = -32601;

    /// <summary>An argument was missing, of the wrong type, or out of range.</summary>
    public const int InvalidParams = -32602;

    /// <summary>The handle in a <c>$ref</c> is not in this session's handle table.</summary>
    public const int HandleNotFound = -32001;

    /// <summary>The handle was released, or its idle timeout elapsed.</summary>
    public const int HandleExpired = -32002;

    /// <summary>The handle resolved, but not to the type the member declares.</summary>
    public const int HandleTypeMismatch = -32003;

    /// <summary>The handle belongs to a different session or a different instrument.</summary>
    public const int HandleForeign = -32004;

    /// <summary>The member was called the wrong way, e.g. subscribing to a plain call.</summary>
    public const int WrongMemberKind = -32005;

    /// <summary>The session does not hold the control lease, and the member would change state.</summary>
    public const int ControlRequired = -32006;

    /// <summary>No device or handle goes by that id.</summary>
    public const int TargetNotFound = -32000;

    /// <summary>The request was cancelled, by the client or because the session ended.</summary>
    public const int RequestCancelled = -32800;

    /// <summary>The message was not valid JSON.</summary>
    public const int ParseError = -32700;

    /// <summary>The message was valid JSON but not a valid request.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The call failed inside the device.</summary>
    public const int DeviceError = -32010;
}

/// <summary>
/// An error that maps directly onto a JSON-RPC error object. Everything a script author needs in
/// order to fix the call goes in <see cref="ErrorData"/>; the message stays short.
/// </summary>
public sealed class ScriptApiException : Exception
{
    /// <summary>Creates an error carrying a JSON-RPC code and optional structured detail.</summary>
    public ScriptApiException(int code, string message, JsonObject errorData = null)
        : base(message)
    {
        Code = code;
        ErrorData = errorData;
    }

    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; }

    /// <summary>Structured detail for <c>error.data</c>, or null.</summary>
    public JsonObject ErrorData { get; }

    /// <summary>The target has no member by that name.</summary>
    public static ScriptApiException MemberNotFound(string wireTypeName, string member) =>
        new(ScriptApiErrorCodes.MemberNotFound,
            $"'{wireTypeName}' has no member '{member}'",
            new JsonObject { ["type"] = wireTypeName, ["member"] = member });

    /// <summary>A required argument was absent.</summary>
    public static ScriptApiException MissingArgument(string member, string parameter) =>
        new(ScriptApiErrorCodes.InvalidParams,
            $"'{member}' requires the argument '{parameter}'",
            new JsonObject { ["member"] = member, ["parameter"] = parameter, ["problem"] = "missing" });

    /// <summary>An argument was present but of the wrong shape.</summary>
    public static ScriptApiException WrongArgumentType(string member, string parameter, string expected, string actual) =>
        new(ScriptApiErrorCodes.InvalidParams,
            $"'{member}' expects '{parameter}' to be {expected}, but received {actual}",
            new JsonObject
            {
                ["member"] = member,
                ["parameter"] = parameter,
                ["problem"] = "type",
                ["expected"] = expected,
                ["actual"] = actual,
            });

    /// <summary>Arguments were not sent as a by-name object.</summary>
    public static ScriptApiException ArgumentsNotAnObject(string member, string actual) =>
        new(ScriptApiErrorCodes.InvalidParams,
            $"'{member}' expects arguments to be passed by name, but received {actual}",
            new JsonObject { ["member"] = member, ["problem"] = "params", ["actual"] = actual });

    /// <summary>A call was subscribed to, or a stream was called.</summary>
    public static ScriptApiException WrongMemberKind(string wireTypeName, string member, string expected) =>
        new(ScriptApiErrorCodes.WrongMemberKind,
            $"'{wireTypeName}.{member}' is not {expected}",
            new JsonObject { ["type"] = wireTypeName, ["member"] = member, ["expected"] = expected });

    /// <summary>A reference resolved to an object of the wrong type.</summary>
    public static ScriptApiException HandleTypeMismatch(string parameter, string expectedWireTypeName, string actual) =>
        new(ScriptApiErrorCodes.HandleTypeMismatch,
            $"'{parameter}' expects a reference to '{expectedWireTypeName}', but received {actual}",
            new JsonObject
            {
                ["parameter"] = parameter,
                ["expected"] = expectedWireTypeName,
                ["actual"] = actual,
            });
}
