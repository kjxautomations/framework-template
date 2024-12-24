using System.ComponentModel;
using System.Runtime.CompilerServices;
using KJX.ProjectTemplate.Devices.FirmwareProtocol;
using Microsoft.Extensions.Logging;

namespace KJX.ProjectTemplate.Devices;

public class FirmwareLed : ILed, ISupportsInitialization, INotifyPropertyChanged
{
    private bool _enabled;
    private readonly LedType _ledType;
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
            var error = false;
            _connection.SendCommandGetResponse(new Request
                {
                    LedControl = new LedControl(){ LedType = _ledType, LedStatus = value ? LedStatus.On : LedStatus.Off }
                },
                (Response resp) =>
                {
                    if (resp.Ack is not null)
                        SetField(ref _enabled, value);
                    else if (resp.Nak is not null)
                    {
                        Logger.LogError("Failed to set LED status: {0}", resp.Nak.ErrorCode);
                        error = true;
                    }
                });
            if (error)
                throw new ApplicationException("Error setting LED status");
            
        } 
    }

    public bool IsInitialized { get; set; }
    
    public FirmwareLed(FirmwareConnection connection, LedType ledType)
    {
        _connection = connection;
        _ledType = ledType;
    }

    FirmwareConnection _connection { get; set; }

    public void Initialize()
    {
        Enabled = false;
        IsInitialized = true;
    }

    public void Shutdown()
    {
        Enabled = false;
    }

    public ushort InitializationGroup => (ushort)(_connection.InitializationGroup + 1);
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}