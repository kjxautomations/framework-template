using System.Text.Json;
using System.Text.Json.Nodes;
using KJX.Devices;
using KJX.Devices.Generated;
using KJX.Scripting.Runtime;
using Microsoft.Extensions.Logging;
using Moq;

namespace KJX.Tests;

/// <summary>
/// The scripting descriptor and dispatch tables are generated from the device interfaces at build
/// time. These tests run them against a real simulated device, so a change to an interface that
/// breaks the script API fails here rather than at the far end of a socket.
/// </summary>
[TestFixture]
public class TestGeneratedScriptApi
{
    private static readonly JsonElement NoArguments = JsonDocument.Parse("{}").RootElement;

    [Test]
    public void Every_marked_device_interface_has_a_dispatcher()
    {
        Assert.That(ScriptApiRegistry.Dispatchers.Select(dispatcher => dispatcher.WireTypeName),
            Is.EquivalentTo(new[] { "device", "led", "motor", "sensor", "stepper_motor" }));
    }

    [Test]
    public void The_descriptor_carries_the_hash_of_its_own_contents()
    {
        var document = JsonNode.Parse(ScriptApiRegistry.DescriptorJson);

        Assert.That(document!["hash"]!.GetValue<string>(), Is.EqualTo(ScriptApiRegistry.DescriptorHash));
        Assert.That(ScriptApiRegistry.DescriptorHash, Does.StartWith("sha256:"));
    }

    [Test]
    public void Members_of_a_base_device_interface_resolve_to_its_own_dispatcher()
    {
        var found = ScriptApiRegistry.TryGetMember("sensor", "initialize", out var dispatcher, out var kind);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(dispatcher.WireTypeName, Is.EqualTo("device"),
                "initialize comes from IDevice, so it is dispatched there rather than repeated on every device.");
            Assert.That(kind, Is.EqualTo(ScriptApiMemberKind.Call));
        });
    }

    [Test]
    public async Task A_generated_call_reaches_a_simulated_sensor()
    {
        var sensor = NewSensor();

        var name = await Invoke(sensor, "sensor", "get_name");
        await Invoke(sensor, "sensor", "read_sensor");
        var value = await Invoke(sensor, "sensor", "get_value");

        Assert.Multiple(() =>
        {
            Assert.That(name!.GetValue<string>(), Is.EqualTo("TemperatureSensor1"));
            Assert.That(value!.GetValue<double>(), Is.InRange(0.0, 100.0));
        });
    }

    [Test]
    public async Task The_sensor_stream_is_dispatched_as_a_subscription()
    {
        var sensor = NewSensor();

        Assert.That(ScriptApiRegistry.TryGetMember("sensor", "readings", out var dispatcher, out var kind), Is.True);
        Assert.That(kind, Is.EqualTo(ScriptApiMemberKind.Stream));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var readings = new List<JsonNode>();

        var reader = Task.Run(async () =>
        {
            await foreach (var reading in dispatcher.Subscribe(
                               sensor, "readings", NoArguments, null, cancellation.Token))
            {
                readings.Add(reading);
                if (readings.Count == 2)
                    break;
            }
        }, cancellation.Token);

        while (!reader.IsCompleted)
        {
            sensor.ReadSensor();
            await Task.Delay(5, cancellation.Token);
        }

        await reader;

        Assert.That(readings, Has.Count.EqualTo(2));
        Assert.That(readings[0]!["value"], Is.Not.Null);
        Assert.That(readings[0]!["timestamp"], Is.Not.Null);
    }

    [Test]
    public void A_bad_argument_is_refused_before_it_reaches_the_device()
    {
        var motor = new SimulatedLinearStepperMotor
        {
            Acceleration = 1.0,
            Velocity = 1.0,
            Logger = new Mock<ILogger<SimulatedLinearStepperMotor>>().Object,
        };

        using var arguments = JsonDocument.Parse("""{"new_position": "over there"}""");

        var error = Assert.CatchAsync<ScriptApiException>(async () =>
            await Dispatcher("motor").InvokeAsync(
                motor, "move_to", arguments.RootElement, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(ScriptApiErrorCodes.InvalidParams));
            Assert.That(error.ErrorData!["parameter"]!.GetValue<string>(), Is.EqualTo("new_position"));
        });
    }

    private static SimulatedTemperatureSensor NewSensor() => new("TemperatureSensor1")
    {
        Logger = new Mock<ILogger<SimulatedTemperatureSensor>>().Object,
    };

    private static IScriptApiDispatcher Dispatcher(string wireTypeName)
    {
        Assert.That(ScriptApiRegistry.TryGetType(wireTypeName, out var dispatcher), Is.True);
        return dispatcher;
    }

    private static async Task<JsonNode> Invoke(object target, string wireTypeName, string member) =>
        await Dispatcher(wireTypeName).InvokeAsync(target, member, NoArguments, null, CancellationToken.None);
}
