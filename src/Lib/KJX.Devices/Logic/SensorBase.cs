using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KJX.Devices.Logic;

public abstract class SensorBase : DeviceBase, ISensor
{
    private double _value;
    private Func<double> _syntheticValueFunction;

    public string Name { get; init; }
    
    public double Value
    {
        get
        {
            if (_syntheticValueFunction != null)
                return _syntheticValueFunction();
            return _value;
        }
        protected set => SetField(ref _value, value);
    }

    public abstract void ReadSensor();
    public event Action<double> ValueUpdated;

    public void OverrideSensorValue(Func<double> valueFunction)
    {
        _syntheticValueFunction = valueFunction;
    }

    protected SensorBase(string name = "")
    {
        Name = name;
    }
    
    protected void FireValueUpdated()
    {
        var listeners = ValueUpdated;
        listeners?.Invoke(Value);
    }
}