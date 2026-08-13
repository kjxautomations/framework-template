using System.Net.Sockets;
using System.Text.Json.Nodes;
using Autofac;
using KJX.Config;
using KJX.Core;
using KJX.Devices;
using KJX.Scripting.Rpc;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;

namespace KJX.Tests;

/// <summary>
/// End-to-end tests of the scripting host: a real WebSocket, real JSON-RPC and the real
/// generated dispatch, over the devices a configuration file declares.
/// </summary>
[TestFixture]
public class TestScriptApiHost
{
    private const string Token = "test-token";

    private IContainer _container;
    private ScriptApiHost _host;
    private string _socketPath;

    [SetUp]
    public async Task SetUp()
    {
        _container = BuildContainer();
        _socketPath = Path.Combine(Path.GetTempPath(), $"kjx-{Guid.NewGuid():N}.sock");

        _host = ScriptApiHost.Create(
            _container,
            new ScriptApiHostOptions
            {
                Address = "127.0.0.1",
                Port = FreePort(),
                Token = Token,
                UnixSocketPath = _socketPath,
                HandleIdleTimeout = TimeSpan.Zero,
            },
            Catalogs,
            _container.Resolve<ILoggerFactory>());

        await _host.StartAsync();
    }

    /// <summary>Finds a port the test can bind, so runs do not collide.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_host != null)
            await _host.DisposeAsync();

        _container?.Dispose();
    }

    private static IScriptApiCatalog[] Catalogs =>
    [
        KJX.Devices.Generated.ScriptApiCatalog.Instance,
        KJX.Tests.Generated.ScriptApiCatalog.Instance,
    ];

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();

        builder.RegisterType<LoggerFactory>().As<ILoggerFactory>().SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();

        using var stream = File.OpenRead(Path.Combine("ConfigTestFiles", "ScriptApiDevices.ini"));
        new ConfigurationHandler().PopulateContainerBuilder(builder, ConfigLoader.LoadConfig(stream));

        return builder.Build();
    }

    private Task<TestRpcClient> ConnectAsync() => TestRpcClient.ConnectAsync(_host.WebSocketEndpoint, Token);

    // ------------------------------------------------------------------ describe

    [Test]
    public async Task Describe_reports_the_devices_the_configuration_declared()
    {
        await using var client = await ConnectAsync();

        var described = await client.CallAsync("describe");
        var devices = described!["devices"]!.AsArray()
            .ToDictionary(
                device => device!["id"]!.GetValue<string>(),
                device => device!["types"]!.AsArray().Select(type => type!.GetValue<string>()).ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(described["hash"]!.GetValue<string>(), Does.StartWith("sha256:"));

            Assert.That(devices["dev/XMotor"], Is.EqualTo(new[] { "motor", "supports_homing" }),
                "XMotor is configured with both interfaces, so it is addressable as both.");

            Assert.That(devices["dev/YMotor"], Is.EqualTo(new[] { "motor" }),
                "YMotor is the same driver without the homing interface in its configuration.");

            Assert.That(devices["dev/TemperatureSensor1"], Is.EqualTo(new[] { "sensor" }));

            Assert.That(devices.Keys, Is.EquivalentTo(new[]
            {
                "dev/Bench", "dev/TemperatureSensor1", "dev/XMotor", "dev/YMotor",
            }));
        });
    }

    [Test]
    public async Task Describe_merges_the_catalogs_of_every_assembly()
    {
        await using var client = await ConnectAsync();

        var described = await client.CallAsync("describe");
        var types = described!["api"]!["types"]!.AsArray().Select(type => type!["name"]!.GetValue<string>()).ToList();

        Assert.That(types, Does.Contain("motor").And.Contains("script_test_device"),
            "One instrument can expose interfaces from more than one assembly.");
    }

    // ------------------------------------------------------------------ capabilities

    [Test]
    public async Task A_capability_from_a_second_interface_is_callable()
    {
        await using var client = await ConnectAsync();

        // home comes from ISupportsHoming, which IMotor does not inherit. It is reachable
        // because the target carries every interface it was registered under.
        await client.CallAsync("dev/XMotor.home");
        var homed = await client.CallAsync("dev/XMotor.get_is_homed");

        Assert.That(homed!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task A_capability_the_device_was_not_configured_with_is_not_callable()
    {
        await using var client = await ConnectAsync();

        var error = Assert.ThrowsAsync<RpcException>(async () => await client.CallAsync("dev/YMotor.home"));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.MemberNotFound));
            Assert.That(error.ErrorData!["types"]!.AsArray().Select(type => type!.GetValue<string>()),
                Is.EqualTo(new[] { "motor" }));
        });
    }

    [Test]
    public async Task A_member_of_an_inherited_type_is_callable()
    {
        await using var client = await ConnectAsync();

        // initialize is declared on ISupportsInitialization, reached through IDevice.
        await client.CallAsync("dev/XMotor.initialize");
        var initialized = await client.CallAsync("dev/XMotor.get_is_initialized");

        Assert.That(initialized!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task Arguments_are_bound_by_name()
    {
        await using var client = await ConnectAsync();

        await client.CallAsync("dev/XMotor.home");
        await client.CallAsync("dev/XMotor.move_to", new JsonObject { ["new_position"] = 2.5 });
        var position = await client.CallAsync("dev/XMotor.get_position");

        Assert.That(position!.GetValue<double>(), Is.EqualTo(2.5));
    }

    [Test]
    public async Task Positional_arguments_are_refused()
    {
        var raw = await SendRawAsync("""{"jsonrpc":"2.0","id":99,"method":"dev/XMotor.move_to","params":[2.5]}""");

        Assert.That(raw["error"]!["code"]!.GetValue<int>(), Is.EqualTo(ScriptApiErrorCodes.InvalidRequest));
    }

    [Test]
    public async Task A_missing_argument_names_the_parameter()
    {
        await using var client = await ConnectAsync();

        var error = Assert.ThrowsAsync<RpcException>(async () => await client.CallAsync("dev/XMotor.move_to"));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.InvalidParams));
            Assert.That(error.ErrorData!["parameter"]!.GetValue<string>(), Is.EqualTo("new_position"));
        });
    }

    [Test]
    public async Task An_unknown_target_is_reported_as_such()
    {
        await using var client = await ConnectAsync();

        var error = Assert.ThrowsAsync<RpcException>(async () => await client.CallAsync("dev/Nope.get_position"));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.TargetNotFound));
    }

    [Test]
    public async Task Malformed_json_is_answered_rather_than_dropped()
    {
        var response = await SendRawAsync("{not json");

        Assert.That(response["error"]!["code"]!.GetValue<int>(), Is.EqualTo(ScriptApiErrorCodes.ParseError));
    }

    // ------------------------------------------------------------------ streams

    [Test]
    public async Task A_stream_delivers_values_until_it_is_unsubscribed()
    {
        await using var client = await ConnectAsync();

        var subscribed = await client.CallAsync("subscribe",
            new JsonObject { ["target"] = "dev/Bench", ["member"] = "ticks" });

        var id = subscribed!["subscription"]!.GetValue<string>();

        var first = await client.NextStreamAsync(id);
        var second = await client.NextStreamAsync(id);

        await client.CallAsync("unsubscribe", new JsonObject { ["subscription"] = id });

        Assert.Multiple(() =>
        {
            Assert.That(first!["value"]!.GetValue<int>(), Is.EqualTo(0));
            Assert.That(second!["value"]!.GetValue<int>(), Is.EqualTo(1));
        });

        // Unsubscribing cancels the token the device is enumerating with, so the stream ends.
        var completion = await WaitForStreamAsync(client, id, notification => notification["complete"] != null);
        Assert.That(completion["complete"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task A_sensor_stream_carries_the_generated_dto()
    {
        await using var client = await ConnectAsync();

        var subscribed = await client.CallAsync("subscribe",
            new JsonObject { ["target"] = "dev/TemperatureSensor1", ["member"] = "readings" });

        var id = subscribed!["subscription"]!.GetValue<string>();

        // The simulated sensor only produces readings once it is running.
        await client.CallAsync("dev/TemperatureSensor1.initialize");

        var reading = await client.NextStreamAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(reading!["value"]!["value"], Is.Not.Null);
            Assert.That(reading["value"]!["timestamp"], Is.Not.Null);
        });

        await client.CallAsync("dev/TemperatureSensor1.shutdown");
    }

    [Test]
    public async Task Subscribing_to_a_call_is_refused()
    {
        await using var client = await ConnectAsync();

        var error = Assert.ThrowsAsync<RpcException>(async () => await client.CallAsync("subscribe",
            new JsonObject { ["target"] = "dev/XMotor", ["member"] = "get_position" }));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.WrongMemberKind));
    }

    // ------------------------------------------------------------------ cancellation

    [Test]
    public async Task An_in_flight_call_can_be_cancelled()
    {
        await using var client = await ConnectAsync();

        var (id, response) = await client.BeginCallAsync("dev/Bench.wait_for_signal");

        // The call is awaiting a signal that never comes; cancellation is what ends it.
        await Task.Delay(100);
        await client.CancelAsync(id);

        var error = Assert.ThrowsAsync<RpcException>(async () => await response);
        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.RequestCancelled));
    }

    // ------------------------------------------------------------------ handles

    [Test]
    public async Task An_object_returned_by_a_call_becomes_a_handle()
    {
        await using var client = await ConnectAsync();

        var claim = await client.CallAsync("dev/Bench.claim", new JsonObject { ["reason"] = "washing" });

        Assert.Multiple(() =>
        {
            Assert.That(claim!["$ref"]!.GetValue<string>(), Does.StartWith("h/"));
            Assert.That(claim["$type"]!.GetValue<string>(), Is.EqualTo("script_test_claim"));
        });

        var handle = claim["$ref"]!.GetValue<string>();
        var reason = await client.CallAsync($"{handle}.get_reason");

        Assert.That(reason!.GetValue<string>(), Is.EqualTo("washing"));
    }

    [Test]
    public async Task Releasing_a_handle_disposes_what_it_pointed_at()
    {
        await using var client = await ConnectAsync();

        var claim = await client.CallAsync("dev/Bench.claim", new JsonObject { ["reason"] = "washing" });
        var handle = claim!["$ref"]!.GetValue<string>();

        await client.CallAsync($"{handle}.use");
        await client.CallAsync("release", new JsonObject { ["handle"] = handle });

        var released = Bench.Claims.Single();
        Assert.That(released.IsReleased, Is.True);

        var error = Assert.ThrowsAsync<RpcException>(async () => await client.CallAsync($"{handle}.use"));
        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.HandleNotFound));
    }

    [Test]
    public async Task A_dropped_connection_releases_the_handles_it_held()
    {
        var client = await ConnectAsync();

        var claim = await client.CallAsync("dev/Bench.claim", new JsonObject { ["reason"] = "mid-run" });
        var handle = claim!["$ref"]!.GetValue<string>();
        await client.CallAsync($"{handle}.use");

        var held = Bench.Claims.Single();
        Assert.That(held.IsReleased, Is.False, "still held while the session is alive");

        // The client dies without a close handshake, the way a crashed script does.
        client.Kill();

        Assert.That(await WaitForAsync(() => held.IsReleased), Is.True,
            "A crashed client must not leave a claim held.");
    }

    [Test]
    public async Task Handles_belong_to_the_session_that_minted_them()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();

        var claim = await first.CallAsync("dev/Bench.claim", new JsonObject { ["reason"] = "mine" });
        var handle = claim!["$ref"]!.GetValue<string>();

        var error = Assert.ThrowsAsync<RpcException>(async () => await second.CallAsync($"{handle}.get_reason"));

        Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.HandleNotFound));
    }

    // ------------------------------------------------------------------ control lease

    [Test]
    public async Task A_second_session_can_read_but_not_change_the_instrument()
    {
        await using var operating = await ConnectAsync();
        await using var watching = await ConnectAsync();

        await operating.CallAsync("dev/XMotor.home");

        // Reading is always allowed.
        var position = await watching.CallAsync("dev/XMotor.get_position");
        Assert.That(position!.GetValue<double>(), Is.EqualTo(0));

        var error = Assert.ThrowsAsync<RpcException>(async () =>
            await watching.CallAsync("dev/XMotor.move_to", new JsonObject { ["new_position"] = 1.0 }));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.ControlRequired));
            Assert.That(error.ErrorData!["holder"], Is.Not.Null);
        });
    }

    [Test]
    public async Task Watching_sessions_can_still_subscribe()
    {
        await using var operating = await ConnectAsync();
        await using var watching = await ConnectAsync();

        await operating.CallAsync("dev/XMotor.home");

        var subscribed = await watching.CallAsync("subscribe",
            new JsonObject { ["target"] = "dev/Bench", ["member"] = "ticks" });

        var id = subscribed!["subscription"]!.GetValue<string>();
        var tick = await watching.NextStreamAsync(id);

        Assert.That(tick!["value"]!.GetValue<int>(), Is.EqualTo(0));
        await watching.CallAsync("unsubscribe", new JsonObject { ["subscription"] = id });
    }

    [Test]
    public async Task Control_passes_on_when_the_holder_goes_quiet()
    {
        // A short lease so the test does not have to wait out the default.
        await _host.DisposeAsync();
        _host = ScriptApiHost.Create(
            _container,
            new ScriptApiHostOptions
            {
                Address = "127.0.0.1",
                Port = FreePort(),
                Token = Token,
                UnixSocketPath = null,
                HandleIdleTimeout = TimeSpan.Zero,
                ControlLeaseTimeout = TimeSpan.FromMilliseconds(150),
            },
            Catalogs,
            _container.Resolve<ILoggerFactory>());

        await _host.StartAsync();

        await using var operating = await ConnectAsync();
        await using var waiting = await ConnectAsync();

        await operating.CallAsync("dev/XMotor.home");

        var refused = Assert.ThrowsAsync<RpcException>(async () =>
            await waiting.CallAsync("dev/XMotor.move_to", new JsonObject { ["new_position"] = 1.0 }));
        Assert.That(refused!.Code, Is.EqualTo(ScriptApiErrorCodes.ControlRequired));

        await Task.Delay(300);

        // The first session has not spoken for longer than the lease allows.
        await waiting.CallAsync("dev/XMotor.move_to", new JsonObject { ["new_position"] = 1.0 });
        var state = await waiting.CallAsync("heartbeat");

        Assert.That(state!["has_control"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task Control_can_be_taken_and_given_back_explicitly()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();

        var taken = await first.CallAsync("acquire_control");
        var refused = await second.CallAsync("acquire_control");

        await first.CallAsync("release_control");
        var granted = await second.CallAsync("acquire_control");

        Assert.Multiple(() =>
        {
            Assert.That(taken!["granted"]!.GetValue<bool>(), Is.True);
            Assert.That(refused!["granted"]!.GetValue<bool>(), Is.False);
            Assert.That(granted!["granted"]!.GetValue<bool>(), Is.True);
        });
    }

    // ------------------------------------------------------------------ transports

    [Test]
    public void A_remote_client_without_a_token_is_refused()
    {
        Assert.CatchAsync(async () => await TestRpcClient.ConnectAsync(_host.WebSocketEndpoint));
        Assert.CatchAsync(async () => await TestRpcClient.ConnectAsync(_host.WebSocketEndpoint, "wrong"));
    }

    [Test]
    public async Task The_unix_socket_serves_the_same_protocol_without_a_token()
    {
        if (!Socket.OSSupportsUnixDomainSockets)
            Assert.Ignore("This platform has no unix domain sockets.");

        await using var client = await TestRpcClient.ConnectUnixAsync(_socketPath);

        var described = await client.CallAsync("describe");

        Assert.That(described!["devices"]!.AsArray(), Is.Not.Empty,
            "On-device clients are authorised by the socket's permissions, not by a token.");
    }

    // ------------------------------------------------------------------ helpers

    private ScriptTestDevice Bench => (ScriptTestDevice)_container.ResolveKeyed<IScriptTestDevice>("Bench");

    private async Task<JsonNode> SendRawAsync(string json)
    {
        await using var client = await ConnectAsync();
        await client.SendRawAsync(json);

        return await client.NextNotificationAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task<JsonNode> WaitForStreamAsync(
        TestRpcClient client, string subscription, Func<JsonNode, bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var notification = await client.NextStreamAsync(subscription, TimeSpan.FromSeconds(10));
            if (predicate(notification))
                return notification;
        }

        throw new TimeoutException("The expected stream notification never arrived.");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(20);
        }

        return condition();
    }
}
