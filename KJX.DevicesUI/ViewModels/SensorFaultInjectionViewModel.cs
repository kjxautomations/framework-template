using KJX.Core.ViewModels;
using KJX.Devices;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.DevicesUI.ViewModels;

public class SensorFaultInjectionViewModel: ViewModelBase
{
    public ISensor Sensor { get; init; }
    [Reactive]
    public bool OverrideValue { get; set; }
    [Reactive]
    public double Value { get; set; }
    
    public SimpleSensorViewModel SensorViewModel { get; init; }

    public SensorFaultInjectionViewModel(ISensor sensor)
    {
        Sensor = sensor;
        SensorViewModel = new SimpleSensorViewModel(sensor);
        this.WhenAnyValue(x => x.OverrideValue).Subscribe(b => ApplyOverrideSensorValue(b));
    }

    private void ApplyOverrideSensorValue(bool apply)
    {
        if (apply)
        {
            Sensor.OverrideSensorValue(() => Value);
        }
        else
        {
            Sensor.OverrideSensorValue(null);
        }
    }
}