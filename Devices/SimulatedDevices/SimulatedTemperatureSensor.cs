using System.Timers;
using Framework.Devices.Logic;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Framework.Devices;

public class SimulatedTemperatureSensor : SensorBase
{
    public required ILogger<SimulatedTemperatureSensor> Logger { get; init; }
    private Timer? _timer;

    public SimulatedTemperatureSensor(string name = "") : base(name)
    {
    }

    protected override void DoInitialize()
    {
        _timer = new System.Timers.Timer(100) { AutoReset = false };
        _timer.Elapsed += ReadSensor;
        _timer.Start();
    }

    protected override void DoShutdown()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Elapsed -= ReadSensor;
            _timer.Dispose();
            _timer = null;
        }
        base.DoShutdown();
    }

    public override void ReadSensor()
    {
        var sensorReadings = new Random();
        Value = sensorReadings.Next(0, 100);
        FireValueUpdated();
        
        _timer?.Start();
    }

    private void ReadSensor(object? sender, ElapsedEventArgs e)
    {
        ReadSensor();
    }
}