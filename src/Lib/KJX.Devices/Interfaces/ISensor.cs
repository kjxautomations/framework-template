using KJX.Scripting;

namespace KJX.Devices;

/// <summary>
/// A single reading taken from a sensor.
/// </summary>
/// <param name="Value">The value that was read.</param>
/// <param name="Timestamp">When the reading was taken.</param>
public readonly record struct SensorReading(double Value, DateTimeOffset Timestamp);

/// <summary>
/// Interface supporting named sensors.
/// </summary>
[ScriptApi]
public interface ISensor : IDevice
{
    /// <summary>
    /// Sensor name
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// The sensor value being read
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Method for reading the sensor value
    /// </summary>
    public void ReadSensor();

    /// <summary>
    /// Every reading as it is taken, including readings identical to the one before, so that a
    /// plot of the stream shows the true sample rate rather than only the changes.
    /// </summary>
    /// <param name="cancellationToken">Ends the subscription.</param>
    /// <returns>
    /// A stream that begins at the first reading taken after enumeration starts. Each subscriber
    /// has its own bounded queue: one that cannot keep up loses its oldest readings rather than
    /// slowing the sensor down or growing without limit.
    /// </returns>
    public IAsyncEnumerable<SensorReading> Readings(CancellationToken cancellationToken = default);
}

/// <summary>
/// Replaces a sensor's value with a synthetic one, for tests and for exercising the UI without
/// hardware. Deliberately kept off <see cref="ISensor"/>: a callback cannot cross the script
/// boundary, and this is not something a script should be able to do.
/// </summary>
public interface ISupportsSensorOverride
{
    /// <summary>
    /// A method for overriding the behavior of the sensor for testing purposes
    /// </summary>
    /// <param name="valueFunction">A callback to get the next synthetic value. Pass null to clear</param>
    public void OverrideSensorValue(Func<double> valueFunction);
}
