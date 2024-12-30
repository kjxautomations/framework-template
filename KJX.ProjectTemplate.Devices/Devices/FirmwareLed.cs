using KJX.ProjectTemplate.Devices.FirmwareProtocol;
using KJX.ProjectTemplate.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace KJX.ProjectTemplate.Devices;

public class FirmwareLed(FirmwareConnection connection, LedType ledType)
    : SupportsInitialization, ILed
{
    private bool _enabled;
    public required ILogger<FirmwareLed> Logger { get; init; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!IsInitialized)
            {
                Logger.LogWarning("Cannot set Enabled property before initialization");
                return;
            }

            connection.SendRequest(NodeId.Main, new Request
            {
                LedControl = new LedControl() { LedType = ledType, LedStatus = value ? LedStatus.On : LedStatus.Off }
            });
            SetField(ref _enabled, value);
        }
    }

    protected override void DoInitialize()
    {
        Enabled = false;
    }

    protected override void DoShutdown()
    {
        Enabled = false;
    }

    public new ushort InitializationGroup => (ushort)(connection.InitializationGroup + 1);
}