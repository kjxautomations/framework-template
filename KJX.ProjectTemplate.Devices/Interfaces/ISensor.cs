namespace KJX.ProjectTemplate.Devices;

public interface ISensor : ISupportsInitialization
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
    /// An event that is fired every time the sensor receives a new value
    /// </summary>
    public event Action<double>? ValueUpdated;
    
    /// <summary>
    /// A method for overriding the behavior of the sensor for testing purposes
    /// </summary>
    /// <param name="valueFunction">A callback to get the next synthetic value. Pass null to clear</param>
    public void OverrideSensorValue(Func<double> valueFunction);
}