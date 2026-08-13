using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;

namespace KJX.Scripting.Rpc;

/// <summary>
/// One client connection: reads JSON-RPC requests, dispatches them concurrently, and writes
/// responses and stream notifications back through a single bounded queue.
/// </summary>
internal sealed class ScriptRpcConnection
{
    private const string Version = "2.0";

    private readonly WebSocket _socket;
    private readonly ScriptSession _session;
    private readonly SessionManager _sessions;
    private readonly ScriptApiHostOptions _options;
    private readonly IScriptAudit _audit;
    private readonly ILogger _logger;

    private readonly Channel<JsonNode> _outbound;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();

    private long _nextSubscription;

    public ScriptRpcConnection(
        WebSocket socket,
        ScriptSession session,
        SessionManager sessions,
        ScriptApiHostOptions options,
        IScriptAudit audit,
        ILogger logger)
    {
        _socket = socket;
        _session = session;
        _sessions = sessions;
        _options = options;
        _audit = audit;
        _logger = logger;

        _outbound = Channel.CreateBounded<JsonNode>(new BoundedChannelOptions(options.OutboundQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Serves the connection until the client goes away or the host shuts down.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        var writer = Task.Run(() => WriteLoopAsync(linked.Token), CancellationToken.None);

        try
        {
            await ReadLoopAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (WebSocketException exception)
        {
            _logger.LogDebug(exception, "Session {Session} disconnected.", _session.Id);
        }
        finally
        {
            await _closing.CancelAsync().ConfigureAwait(false);
            _outbound.Writer.TryComplete();

            foreach (var subscription in _subscriptions.Values)
                await subscription.DisposeAsync().ConfigureAwait(false);

            foreach (var request in _inFlight.Values)
                await request.CancelAsync().ConfigureAwait(false);

            try
            {
                await writer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            // The backstop: whatever the script claimed is released here, however it ended.
            await _session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (message == null)
                    return;

                Handle(message, cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads one whole message, however many frames it arrives in.</summary>
    private async Task<string> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();

        while (true)
        {
            var received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

            if (received.MessageType == WebSocketMessageType.Close)
            {
                await CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken).ConfigureAwait(false);
                return null;
            }

            message.Write(buffer, 0, received.Count);

            if (message.Length > _options.MaximumMessageBytes)
            {
                await CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (received.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    private void Handle(string message, CancellationToken cancellationToken)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException exception)
        {
            _ = RespondAsync(null, Error(ScriptApiErrorCodes.ParseError, exception.Message, null));
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                _ = RespondAsync(null, Error(ScriptApiErrorCodes.InvalidRequest,
                    "Each message must be a single JSON-RPC request object.", null));
                return;
            }

            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                _ = RespondAsync(null, Error(ScriptApiErrorCodes.InvalidRequest, "'method' is required.", null));
                return;
            }

            var method = methodElement.GetString();
            var arguments = root.TryGetProperty("params", out var parameters) ? parameters.Clone() : default;

            if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            {
                _ = RespondAsync(IdOf(root), Error(ScriptApiErrorCodes.InvalidRequest,
                    "Arguments are passed by name: 'params' must be an object.", null));
                return;
            }

            // A request without an id is a notification, and the only one a client sends is
            // cancellation.
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind == JsonValueKind.Null)
            {
                if (method == "$/cancelRequest")
                    Cancel(arguments);

                return;
            }

            var id = idElement.Clone();
            var key = id.GetRawText();
            var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _inFlight[key] = requestCancellation;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await InvokeAsync(method, arguments, requestCancellation.Token).ConfigureAwait(false);
                    await RespondAsync(JsonNode.Parse(key), result).ConfigureAwait(false);

                    if (method == "subscribe")
                        StartSubscription(result);
                }
                catch (OperationCanceledException)
                {
                    await RespondAsync(JsonNode.Parse(key), Error(ScriptApiErrorCodes.RequestCancelled, "The request was cancelled.", null)).ConfigureAwait(false);
                }
                catch (ScriptApiException exception)
                {
                    await RespondAsync(JsonNode.Parse(key), Error(exception.Code, exception.Message, exception.ErrorData)).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Session {Session} failed handling '{Method}'.", _session.Id, method);
                    await RespondAsync(JsonNode.Parse(key), Error(ScriptApiErrorCodes.DeviceError, exception.Message,
                        new JsonObject { ["exception"] = exception.GetType().Name })).ConfigureAwait(false);
                }
                finally
                {
                    if (_inFlight.TryRemove(key, out var finished))
                        finished.Dispose();
                }
            }, CancellationToken.None);
        }
    }

    private void Cancel(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("id", out var id))
            return;

        if (_inFlight.TryGetValue(id.GetRawText(), out var request))
            request.Cancel();
    }

    private async Task<JsonNode> InvokeAsync(string method, JsonElement arguments, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "describe":
                return Result(JsonNode.Parse(_session.Surface.Describe(_session.Devices.Devices.Values)));

            case "subscribe":
                return Result(Subscribe(arguments, cancellationToken));

            case "unsubscribe":
                return Result(await UnsubscribeAsync(arguments).ConfigureAwait(false));

            case "release":
                return Result(await ReleaseAsync(arguments).ConfigureAwait(false));

            case "acquire_control":
                return Result(AcquireControl());

            case "release_control":
                _sessions.Lease.Release(_session);
                return Result(LeaseState());

            case "heartbeat":
                _sessions.Lease.Touch(_session);
                return Result(LeaseState());

            default:
                return Result(await CallAsync(method, arguments, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<JsonNode> CallAsync(string method, JsonElement arguments, CancellationToken cancellationToken)
    {
        var (targetId, member) = Split(method);
        var target = _session.ResolveTarget(targetId);
        var resolved = Resolve(target, member);

        if (resolved.Kind != ScriptApiMemberKind.Call)
            throw ScriptApiException.WrongMemberKind(targetId, member, "a call");

        RequireControlFor(resolved, targetId, member);

        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();

        try
        {
            var result = await resolved.Dispatcher
                .InvokeAsync(target.Instance, member, arguments, _session.References, cancellationToken)
                .ConfigureAwait(false);

            _audit.Invoked(_session, targetId, member, arguments, result, started, clock.Elapsed, null);
            return result;
        }
        catch (Exception exception)
        {
            _audit.Invoked(_session, targetId, member, arguments, null, started, clock.Elapsed, exception);
            throw;
        }
    }

    private JsonNode Subscribe(JsonElement arguments, CancellationToken cancellationToken)
    {
        var targetId = RequiredString(arguments, "target", "subscribe");
        var member = RequiredString(arguments, "member", "subscribe");
        var target = _session.ResolveTarget(targetId);
        var resolved = Resolve(target, member);

        if (resolved.Kind != ScriptApiMemberKind.Stream)
            throw ScriptApiException.WrongMemberKind(targetId, member, "a stream");

        var memberArguments = arguments.TryGetProperty("params", out var declared) ? declared.Clone() : default;
        var id = "sub/" + Interlocked.Increment(ref _nextSubscription);

        var subscription = new Subscription(id);
        _subscriptions[id] = subscription;

        var started = DateTimeOffset.UtcNow;
        _audit.Invoked(_session, targetId, member, memberArguments, JsonValue.Create(id), started, TimeSpan.Zero, null);

        subscription.PumpFactory = () => PumpAsync(subscription, resolved, target, member, memberArguments);

        return new JsonObject { ["subscription"] = id };
    }

    private async Task PumpAsync(
        Subscription subscription,
        ResolvedMember resolved,
        ScriptTarget target,
        string member,
        JsonElement arguments)
    {
        try
        {
            var stream = resolved.Dispatcher.Subscribe(
                target.Instance, member, arguments, _session.References, subscription.Cancellation.Token);

            await foreach (var value in stream.ConfigureAwait(false))
            {
                var notification = new JsonObject
                {
                    ["subscription"] = subscription.Id,
                    ["value"] = value,
                };

                var dropped = Interlocked.Exchange(ref subscription.Dropped, 0);
                if (dropped > 0)
                    notification["dropped"] = dropped;

                if (!TrySendStream(notification))
                {
                    // The client is not keeping up. Drop this value and tell it how many it
                    // missed rather than letting the queue grow.
                    Interlocked.Add(ref subscription.Dropped, dropped + 1);
                }
            }

            TrySendStream(new JsonObject { ["subscription"] = subscription.Id, ["complete"] = true });
        }
        catch (OperationCanceledException)
        {
            TrySendStream(new JsonObject { ["subscription"] = subscription.Id, ["complete"] = true });
        }
        catch (Exception exception)
        {
            var code = exception is ScriptApiException script ? script.Code : ScriptApiErrorCodes.DeviceError;

            TrySendStream(new JsonObject
            {
                ["subscription"] = subscription.Id,
                ["error"] = new JsonObject { ["code"] = code, ["message"] = exception.Message },
            });
        }
        finally
        {
            _subscriptions.TryRemove(subscription.Id, out _);
        }
    }

    private async Task<JsonNode> UnsubscribeAsync(JsonElement arguments)
    {
        var id = RequiredString(arguments, "subscription", "unsubscribe");

        if (!_subscriptions.TryRemove(id, out var subscription))
        {
            throw new ScriptApiException(
                ScriptApiErrorCodes.TargetNotFound,
                $"'{id}' is not an open subscription",
                new JsonObject { ["subscription"] = id });
        }

        await subscription.DisposeAsync().ConfigureAwait(false);
        return new JsonObject { ["subscription"] = id, ["closed"] = true };
    }

    /// <summary>
    /// Releases one handle or a batch of them. The batch form is what the Python client uses to
    /// flush the handles its garbage collector found, piggybacked on a later call.
    /// </summary>
    private async Task<JsonNode> ReleaseAsync(JsonElement arguments)
    {
        var released = new JsonArray();
        string id;

        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("handles", out var handles) &&
            handles.ValueKind == JsonValueKind.Array)
        {
            foreach (var handle in handles.EnumerateArray())
            {
                id = handle.GetString();

                try
                {
                    await _session.Handles.ReleaseAsync(id).ConfigureAwait(false);
                    released.Add(id);
                }
                catch (ScriptApiException exception) when (exception.Code == ScriptApiErrorCodes.HandleNotFound)
                {
                    // A finalizer batch may contain a handle the idle sweeper already reclaimed.
                    // Keep processing so one stale id cannot strand the rest of the batch.
                }
            }

            return new JsonObject { ["released"] = released };
        }

        id = RequiredString(arguments, "handle", "release");
        await _session.Handles.ReleaseAsync(id).ConfigureAwait(false);
        released.Add(id);

        return new JsonObject { ["released"] = released };
    }

    private JsonNode AcquireControl()
    {
        var granted = _sessions.Lease.TryAcquire(_session);
        var state = LeaseState();
        state["granted"] = granted;
        return state;
    }

    private JsonObject LeaseState() => new()
    {
        ["session"] = _session.Id,
        ["has_control"] = _session.HasControl,
        ["holder"] = _sessions.Lease.Holder?.Id,
    };

    /// <summary>
    /// Reading the instrument is always allowed. Changing it needs the lease, which a session
    /// takes implicitly the first time it tries, provided nobody else holds it.
    /// </summary>
    private void RequireControlFor(ResolvedMember resolved, string target, string member)
    {
        if (!resolved.ChangesState)
            return;

        if (!_sessions.Lease.TryAcquire(_session))
        {
            throw new ScriptApiException(
                ScriptApiErrorCodes.ControlRequired,
                $"'{target}.{member}' changes the instrument, and session " +
                $"'{_sessions.Lease.Holder?.Id}' holds control",
                new JsonObject
                {
                    ["target"] = target,
                    ["member"] = member,
                    ["holder"] = _sessions.Lease.Holder?.Id,
                });
        }

        _sessions.Lease.Touch(_session);
    }

    private ResolvedMember Resolve(ScriptTarget target, string member)
    {
        if (_session.Surface.TryResolveMember(target.WireTypes, member, out var resolved))
            return resolved;

        throw new ScriptApiException(
            ScriptApiErrorCodes.MemberNotFound,
            $"'{target.Id}' has no member '{member}'",
            new JsonObject
            {
                ["target"] = target.Id,
                ["member"] = member,
                ["types"] = new JsonArray(target.WireTypes.Select(type => (JsonNode)type).ToArray()),
            });
    }

    private static (string Target, string Member) Split(string method)
    {
        var separator = method.LastIndexOf('.');

        if (separator <= 0 || separator == method.Length - 1)
        {
            throw new ScriptApiException(
                ScriptApiErrorCodes.MemberNotFound,
                $"'{method}' is not a known method. Calls are addressed as '<target>.<member>'.",
                new JsonObject { ["method"] = method });
        }

        return (method.Substring(0, separator), method.Substring(separator + 1));
    }

    private static string RequiredString(JsonElement arguments, string name, string method)
    {
        var value = ScriptApiJson.Required(arguments, name, method);

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw ScriptApiException.WrongArgumentType(method, name, "a string", ScriptApiJson.Describe(value));
    }

    private static JsonNode Result(JsonNode value) => new JsonObject { ["result"] = value };

    private static JsonNode Error(int code, string message, JsonObject data)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data != null)
            error["data"] = data;

        return new JsonObject { ["error"] = error };
    }

    /// <summary>Queues a response. Responses wait for room rather than being dropped.</summary>
    private async Task RespondAsync(JsonNode id, JsonNode payload)
    {
        var message = new JsonObject { ["jsonrpc"] = Version, ["id"] = id };

        foreach (var property in payload.AsObject().ToList())
            message[property.Key] = property.Value?.DeepClone();

        try
        {
            await _outbound.Writer.WriteAsync(message, _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the connection is going away
        }
        catch (ChannelClosedException)
        {
            // the connection is going away
        }
    }

    private void StartSubscription(JsonNode result)
    {
        if (result["result"]?["subscription"]?.GetValue<string>() is not { } id ||
            !_subscriptions.TryGetValue(id, out var subscription))
        {
            return;
        }

        subscription.Pump = Task.Run(subscription.PumpFactory, CancellationToken.None);
    }

    private bool TrySendStream(JsonObject parameters) =>
        _outbound.Writer.TryWrite(new JsonObject
        {
            ["jsonrpc"] = Version,
            ["method"] = "$/stream",
            ["params"] = parameters,
        });

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await _socket.CloseAsync(status, reason, cancellationToken).ConfigureAwait(false);
    }

    private static JsonNode IdOf(JsonElement root) =>
        root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(id.GetRawText())
            : null;

    private sealed class Subscription(string id) : IAsyncDisposable
    {
        public string Id { get; } = id;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task Pump;

        public Func<Task> PumpFactory;

        public long Dropped;

        public async ValueTask DisposeAsync()
        {
            await Cancellation.CancelAsync().ConfigureAwait(false);

            if (Pump != null)
            {
                try
                {
                    await Pump.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // the pump reports its own failures to the client
                }
            }

            Cancellation.Dispose();
        }
    }
}
