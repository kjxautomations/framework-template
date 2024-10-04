using System;
using System.Collections.ObjectModel;
using Framework.Core.ViewModels;
using DevicesUI.ViewModels;
using Framework.Devices;

namespace FrameworkSample.ViewModels;

public class DeviceShowcaseViewModel : ViewModelBase
{
    public string StateLabel { get; } = "Welcome to the Device Showcase!";
    public ObservableCollection<SimpleMotorControlViewModel> SimpleMotorControlViewModels { get; init; } = [];
    public ObservableCollection<SimpleSensorViewModel> SensorViewModels { get; init; } = [];
    public ObservableCollection<SimpleCameraViewModel> CameraViewModels { get; init; } = [];


    public DeviceShowcaseViewModel(ISensor[] sensors, IMotor[] motors, ICamera[] cameras)
    {
        foreach (var sensor in sensors)
        {
            SensorViewModels.Add(new SimpleSensorViewModel(sensor));
        }
    
        foreach (var motor in motors)
        {
            SimpleMotorControlViewModels.Add(new SimpleMotorControlViewModel(motor));
        }

        try
        {
            foreach (var camera in cameras)
            {
                CameraViewModels.Add(new SimpleCameraViewModel(camera));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine(e.StackTrace);
            throw;
        }
        
    }
}