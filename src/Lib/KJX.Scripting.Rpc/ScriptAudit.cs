using System.Text.Json;
using System.Text.Json.Nodes;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;

namespace KJX.Scripting.Rpc;

/// <summary>
/// Records what scripts did. Every invocation is logged with who asked, what they asked for and
/// what came back, because on an instrument the question after the fact is always "what actually
/// ran".
/// </summary>
public interface IScriptAudit
{
    /// <summary>Records one call or subscription attempt.</summary>
    void Invoked(
        ScriptSession session,
        string target,
        string member,
        JsonElement arguments,
        JsonNode result,
        DateTimeOffset started,
        TimeSpan elapsed,
        Exception error);
}

/// <summary>Writes the audit trail through the logging pipeline.</summary>
public sealed class LoggingScriptAudit : IScriptAudit
{
    private readonly ILogger<LoggingScriptAudit> _logger;

    /// <summary>Creates the audit sink.</summary>
    public LoggingScriptAudit(ILogger<LoggingScriptAudit> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Invoked(
        ScriptSession session,
        string target,
        string member,
        JsonElement arguments,
        JsonNode result,
        DateTimeOffset started,
        TimeSpan elapsed,
        Exception error)
    {
        var argumentText = arguments.ValueKind == JsonValueKind.Undefined ? "{}" : arguments.GetRawText();

        if (error == null)
        {
            _logger.LogInformation(
                "script {Session} {Principal} {Target}.{Member} {Arguments} -> {Result} in {Elapsed}ms at {Started:O}",
                session.Id, session.Principal, target, member, argumentText,
                result?.ToJsonString() ?? "null", elapsed.TotalMilliseconds, started);

            return;
        }

        var code = error is ScriptApiException script ? script.Code : ScriptApiErrorCodes.DeviceError;

        _logger.LogWarning(
            error,
            "script {Session} {Principal} {Target}.{Member} {Arguments} -> error {Code} in {Elapsed}ms at {Started:O}",
            session.Id, session.Principal, target, member, argumentText, code, elapsed.TotalMilliseconds, started);
    }
}
