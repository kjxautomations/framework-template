using System;
using System.Collections.ObjectModel;
using KJX.Core.ViewModels;
using KJX.Devices;
using KJX.DevicesUI.ViewModels;

namespace KJX.ProjectTemplate.Engineering.ViewModels;

public class DeviceShowcaseViewModel : ViewModelBase
{
    public string HeaderLabel { get; } = "Available Devices";

    public ObservableCollection<SimpleMotorControlViewModel> Motors { get; } = [];
    public ObservableCollection<SimpleSensorViewModel> Sensors { get; } = [];
    public ObservableCollection<SimpleCameraViewModel> Cameras { get; } = [];

    public DeviceShowcaseViewModel(IMotor[] motors, ISensor[] sensors, ICamera[] cameras)
    {
        try
        {
            foreach (var motor in motors)
            {
                Motors.Add(new SimpleMotorControlViewModel(motor));
            }

            foreach (var sensor in sensors)
            {
                Sensors.Add(new SimpleSensorViewModel(sensor));
            }

            foreach (var camera in cameras)
            {
                Cameras.Add(new SimpleCameraViewModel(camera));
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