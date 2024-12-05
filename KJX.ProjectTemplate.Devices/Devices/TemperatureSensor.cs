using KJX.ProjectTemplate.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace Framework.Devices;

public class TemperatureSensor : SensorBase
{
    public required ILogger<SimulatedTemperatureSensor> Logger { get; init; }
    
    public TemperatureSensor(string name = "") : base(name)
    {
    }

    protected override void DoInitialize()
    {
        Logger.LogInformation("Initializing");
       
    }

    public override void ReadSensor()
    {
        Value = 25.0;
    }
}