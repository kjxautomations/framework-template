using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace KJX.Devices.Logic;

public abstract class DeviceBase : IDevice
{
    private bool _isInitialized;

    public bool IsInitialized
    {
        get => _isInitialized;
        protected set => SetField(ref _isInitialized, value);
    }

    public void Initialize()
    {
        if (IsInitialized) return;
        DoInitialize();
        IsInitialized = true;
    }

    public void Shutdown()
    {
        if (IsInitialized)
        {
            DoShutdown();
            IsInitialized = false;
        }
    }
    

    public ushort InitializationGroup { get; protected init; } = ushort.MaxValue;

    // derived classes implement these methods to perform initialization and (optional) shutdown
    protected abstract void DoInitialize();

    protected virtual void DoShutdown()
    {
    }
    
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetField(ref _isBusy, value);
    }
    
    /// <summary>
    /// Used by derived classes to set "Parameter" values, which are validated and cannot be set
    /// when the device is busy.
    /// </summary>
    protected void SetParameter<T>(ref T field, T value, string propertyName) where T : IConvertible
    {
        if (IsBusy)
            throw new InvalidOperationException($"{propertyName} cannot be set while the device is busy.");

        var property = GetType().GetProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"Property {propertyName} not found.");

        var rangeAttribute = property.GetCustomAttribute<RangeAttribute>(inherit:true);
        if (rangeAttribute != null && !rangeAttribute.IsValid(value))
            throw new ArgumentOutOfRangeException(propertyName, value, 
                $"Value {value} is out of range ({rangeAttribute.Minimum} - {rangeAttribute.Maximum}).");

        field = value;
        OnPropertyChanged(propertyName);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
