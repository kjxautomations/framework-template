using System.Collections.ObjectModel;
using System.Reflection;
using KJX.Devices;
using KJX.Devices.Logic;


public class DeviceSettingsViewModel
{
    public ObservableCollection<DevicePropertyViewModel>? BasicProperties { get; }
    
    public ObservableCollection<KeyValuePair<string, ObservableCollection<DevicePropertyViewModel>>>? 
        AdvancedProperties { get; }
    
    public IDevice? Device { get; }
    public DeviceSettingsViewModel() { }
    
    public bool HasAdvancedProperties => AdvancedProperties?.Count > 0;
    
    public DeviceSettingsViewModel(IDevice device)
    {
        Device = device;
        var properties = device.GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<GroupAttribute>() != null)
            .Select(p => new DevicePropertyViewModel(device, p))
            .ToArray();

        BasicProperties = new ObservableCollection<DevicePropertyViewModel>(
            properties.Where(p => p.Group == "Basic"));
        var advancedProperties = properties.Where(p => p.Group != "Basic")
            .GroupBy(p => p.Group,
                (s, models) => 
                    new KeyValuePair<string,ObservableCollection<DevicePropertyViewModel>>(s, new ObservableCollection<DevicePropertyViewModel>(models)));
            
            
        AdvancedProperties = new ObservableCollection<KeyValuePair<string, ObservableCollection<DevicePropertyViewModel>>>(advancedProperties);
    }
}