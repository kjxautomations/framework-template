using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Avalonia.Threading;
using KJX.Devices;
using KJX.Devices.Logic;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

public class DevicePropertyViewModel : INotifyPropertyChanged
{
    public IDevice Device { get; }
    private PropertyInfo Property { get; }
    private RangeAttribute? _rangeAttribute;


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

            if (_rangeAttribute != null && !_rangeAttribute.IsValid(value))
                throw new ArgumentOutOfRangeException(Name, value, 
                    $"Value {value} is out of range ({_rangeAttribute.Minimum} - {_rangeAttribute.Maximum}).");

            Property.SetValue(Device, Convert.ChangeType(value, Property.PropertyType), null);
            OnPropertyChanged(nameof(Value));
        }
    }

    public double Increment
    {
        get
        {
            if (_rangeAttribute is RangeIncrementAttribute rangeIncrementAttribute)
                return (double)rangeIncrementAttribute.Increment;
            return 1.0;
        }
    }

    public bool IsNumeric => Property.PropertyType == typeof(int) || Property.PropertyType == typeof(double) || Property.PropertyType == typeof(float);
    public bool IsBoolean => Property.PropertyType == typeof(bool);
    public bool IsEnum => Property.PropertyType.IsEnum;
    public bool IsString => Property.PropertyType == typeof(string);
    
    public Array? EnumValues => IsEnum ? Enum.GetValues(Property.PropertyType) : null;

    public DevicePropertyViewModel(IDevice device, PropertyInfo property)
    {
        Device = device;
        Property = property;
        _rangeAttribute = property.GetCustomAttribute<RangeAttribute>(inherit: true);
        if (_rangeAttribute != null)
        {
            Min = Convert.ToDouble(_rangeAttribute.Minimum);
            Max = Convert.ToDouble(_rangeAttribute.Maximum);
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