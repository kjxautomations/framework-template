using KJX.Devices;
using KJX.Devices.Logic;

namespace KJX.Tests;

/// <summary>
/// The reading stream exists so that a plot shows every sample the sensor took. A sensor that
/// reads the same value ten times has produced ten readings, not one.
/// </summary>
[TestFixture]
public class TestSensorReadings
{
    private sealed class StubSensor : SensorBase
    {
        private double _next;

        public StubSensor(string name = "stub") : base(name)
        {
        }

        protected override void DoInitialize()
        {
        }

        public void Produce(double value)
        {
            _next = value;
            ReadSensor();
        }

        public override void ReadSensor()
        {
            Value = _next;
            PublishReading();
        }
    }

    [Test]
    public async Task Unchanged_values_are_still_delivered()
    {
        var sensor = new StubSensor();
        var readings = new List<SensorReading>();
        using var attached = new SemaphoreSlim(0);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var reader = Task.Run(async () =>
        {
            await foreach (var reading in sensor.Readings(cancellation.Token))
            {
                readings.Add(reading);
                attached.Release();
                if (readings.Count == 3)
                    break;
            }
        }, cancellation.Token);

        // The stream starts at the first reading taken after enumeration begins, so produce the
        // same value until the subscriber is known to be attached.
        while (!await attached.WaitAsync(TimeSpan.FromMilliseconds(20), cancellation.Token))
            sensor.Produce(25.0);

        sensor.Produce(25.0);
        sensor.Produce(25.0);

        await reader;

        Assert.That(readings, Has.Count.EqualTo(3));
        Assert.That(readings.Select(reading => reading.Value), Is.All.EqualTo(25.0));
        Assert.That(readings.Select(reading => reading.Timestamp), Is.Ordered);
    }

    [Test]
    public async Task Every_subscriber_gets_its_own_copy_of_the_stream()
    {
        var sensor = new StubSensor();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = Take(3);
        var second = Take(3);

        while (!first.IsCompleted || !second.IsCompleted)
        {
            sensor.Produce(7.0);
            await Task.Delay(5, cancellation.Token);
        }

        Assert.That(await first, Has.Count.EqualTo(3));
        Assert.That(await second, Has.Count.EqualTo(3));

        Task<List<double>> Take(int count) => Task.Run(async () =>
        {
            var values = new List<double>();
            await foreach (var reading in sensor.Readings(cancellation.Token))
            {
                values.Add(reading.Value);
                if (values.Count == count)
                    break;
            }

            return values;
        }, cancellation.Token);
    }

    [Test]
    public void Cancelling_ends_the_subscription()
    {
        var sensor = new StubSensor();
        using var cancellation = new CancellationTokenSource();

        var reader = Task.Run(async () =>
        {
            await foreach (var reading in sensor.Readings(cancellation.Token))
            {
            }
        });

        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await reader);
    }

    [Test]
    public void The_override_hook_is_not_part_of_the_sensor_interface()
    {
        var sensor = new StubSensor();

        Assert.That(sensor, Is.InstanceOf<ISupportsSensorOverride>());
        Assert.That(typeof(ISensor).GetMethod(nameof(ISupportsSensorOverride.OverrideSensorValue)), Is.Null,
            "OverrideSensorValue hands out a callback and must stay off the scripting surface.");

        ((ISupportsSensorOverride)sensor).OverrideSensorValue(() => 42.0);
        Assert.That(sensor.Value, Is.EqualTo(42.0));
    }
}
