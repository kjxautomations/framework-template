using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace KJX.Tests;

/// <summary>An error returned by the host.</summary>
public sealed class RpcException(int code, string message, JsonNode data) : Exception(message)
{
    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; } = code;

    /// <summary>Whatever the host put in error.data.</summary>
    public JsonNode ErrorData { get; } = data;
}

/// <summary>
/// A minimal JSON-RPC client, standing in for the Python transport until stage 4 writes it.
/// Speaks the protocol by hand so the tests exercise the wire format rather than a shared helper.
/// </summary>
internal sealed class TestRpcClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly HttpMessageInvoker _invoker;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode>> _pending = new();
    private readonly Channel<JsonNode> _notifications = Channel.CreateUnbounded<JsonNode>();
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _reader;

    private long _nextId;

    private TestRpcClient(ClientWebSocket socket, HttpMessageInvoker invoker)
    {
        _socket = socket;
        _invoker = invoker;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Connects over TCP.</summary>
    public static async Task<TestRpcClient> ConnectAsync(Uri uri, string token = null)
    {
        var socket = new ClientWebSocket();

        if (token != null)
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await socket.ConnectAsync(uri, timeout.Token);

        return new TestRpcClient(socket, null);
    }

    /// <summary>
    /// Connects over a unix domain socket. The URI host is a placeholder: the connect callback
    /// dials the socket file instead of resolving it.
    /// </summary>
    public static async Task<TestRpcClient> ConnectUnixAsync(string socketPath, string path = "/rpc")
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };

        var invoker = new HttpMessageInvoker(handler);
        var webSocket = new ClientWebSocket();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await webSocket.ConnectAsync(new Uri("ws://localhost" + path), invoker, timeout.Token);

        return new TestRpcClient(webSocket, invoker);
    }

    /// <summary>Calls a method and waits for its response.</summary>
    public async Task<JsonNode> CallAsync(string method, JsonObject parameters = null, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters != null)
            request["params"] = parameters;

        await SendAsync(request.ToJsonString());

        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        await using var registration = cancellation.Token.Register(() =>
            completion.TrySetException(new TimeoutException($"'{method}' did not answer in time.")));

        return await completion.Task;
    }

    /// <summary>Sends a call without waiting, so a test can cancel it.</summary>
    public async Task<(long Id, Task<JsonNode> Response)> BeginCallAsync(string method, JsonObject parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters != null)
            request["params"] = parameters;

        await SendAsync(request.ToJsonString());
        return (id, completion.Task);
    }

    /// <summary>Sends the cancellation notification for an in-flight request.</summary>
    public Task CancelAsync(long id) => SendAsync(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["method"] = "$/cancelRequest",
        ["params"] = new JsonObject { ["id"] = id },
    }.ToJsonString());

    /// <summary>Sends a raw message, for testing malformed input.</summary>
    public Task SendRawAsync(string json) => SendAsync(json);

    /// <summary>Waits for the next server notification.</summary>
    public async Task<JsonNode> NextNotificationAsync(TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        return await _notifications.Reader.ReadAsync(cancellation.Token);
    }

    /// <summary>Waits for the next $/stream notification for a subscription.</summary>
    public async Task<JsonNode> NextStreamAsync(string subscription, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));

        while (true)
        {
            var notification = await _notifications.Reader.ReadAsync(cancellation.Token);

            if (notification["method"]?.GetValue<string>() == "$/stream" &&
                notification["params"]?["subscription"]?.GetValue<string>() == subscription)
            {
                return notification["params"];
            }
        }
    }

    /// <summary>
    /// Drops the connection without a close handshake, the way a crashed script or a yanked
    /// network cable would.
    /// </summary>
    public void Kill()
    {
        _socket.Abort();
        _closing.Cancel();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch (Exception)
        {
            // the test may already have killed it
        }

        await _closing.CancelAsync();

        try
        {
            await _reader;
        }
        catch (Exception)
        {
        }

        _socket.Dispose();
        _invoker?.Dispose();
        _closing.Dispose();
    }

    private async Task SendAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _closing.Token);
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (!_closing.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult received;

                do
                {
                    received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _closing.Token);

                    if (received.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(buffer, 0, received.Count);
                }
                while (!received.EndOfMessage);

                Route(JsonNode.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)));
            }
        }
        catch (Exception exception)
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(exception);
        }
    }

    private void Route(JsonNode message)
    {
        var id = message?["id"];

        if (id == null)
        {
            _notifications.Writer.TryWrite(message);
            return;
        }

        if (!_pending.TryRemove(id.GetValue<long>(), out var completion))
        {
            // A response to something sent raw, which the test reads as a notification.
            _notifications.Writer.TryWrite(message);
            return;
        }

        var error = message["error"];

        if (error != null)
        {
            completion.TrySetException(new RpcException(
                error["code"]!.GetValue<int>(),
                error["message"]?.GetValue<string>() ?? string.Empty,
                error["data"]));

            return;
        }

        completion.TrySetResult(message["result"]);
    }
}
