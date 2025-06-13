using KJX.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace KJX.Devices;

public class SimulatedLed : DeviceBase, ILed
{
    public required ILogger<SimulatedLed> Logger { get; init; }
    public bool Enabled { get; set; }
    protected override void DoInitialize()
    {
        Logger?.LogInformation("SimulatedLed initialized");
    }
}