using System.ComponentModel;
using System.Reflection;
using Avalonia.Threading;
using KJX.Devices;
using KJX.Devices.Logic;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

public class DevicePropertyViewModel : INotifyPropertyChanged
{
    public IDevice Device { get; }
    public PropertyInfo Property { get; }
    private object? _value;
    private RangeAttribute _rangeAttribute;


    public string Name => Property.Name;
    public string? Group => Property.GetCustomAttribute<GroupAttribute>()?.Group ?? null;
    
    [Reactive]
    public bool IsBusy { get; private set; }
    
    public double? Min { get; }
    public double? Max { get; }

    public object? Value
    {
        get => Property.GetValue(Device);
        set
        {
            if (Device.IsBusy)
                throw new InvalidOperationException($"{Name} cannot be set while the device is busy.");

            if (_rangeAttribute != null && !_rangeAttribute.IsValid(value as IConvertible))
                throw new ArgumentOutOfRangeException(Name, value, 
                    $"Value {value} is out of range ({_rangeAttribute.Min} - {_rangeAttribute.Max}).");

            Property.SetValue(Device, Convert.ChangeType(value, Property.PropertyType), null);
            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public double Increment => Property.GetCustomAttribute<RangeAttribute>()?.Increment ?? 1.0;

    public bool IsNumeric => Property.PropertyType == typeof(int) || Property.PropertyType == typeof(double) || Property.PropertyType == typeof(float);
    public bool IsBoolean => Property.PropertyType == typeof(bool);
    public bool IsEnum => Property.PropertyType.IsEnum;
    public bool IsString => Property.PropertyType == typeof(string);
    
    public Array? EnumValues => IsEnum ? Enum.GetValues(Property.PropertyType) : null;

    public DevicePropertyViewModel(IDevice device, PropertyInfo property)
    {
        Device = device;
        Property = property;
        _value = Property.GetValue(device);
        _rangeAttribute = property.GetCustomAttribute<RangeAttribute>();
        if (_rangeAttribute != null)
        {
            Min = _rangeAttribute.Min;
            Max = _rangeAttribute.Max;
        }
        Device.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == "IsBusy")
            {
                var isBusy = Device.IsBusy;
                Dispatcher.UIThread.Post(() => IsBusy = isBusy);
            }

        };

    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}