using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Threading;
using KJX.Devices;
using KJX.Devices.Logic;
using KJX.DevicesUI.Views;
using MsBox.Avalonia.ViewModels.Commands;

public class DeviceSettingsViewModel
{
    public List<DevicePropertyViewModel>? BasicProperties { get; }
    public Dictionary<string, List<DevicePropertyViewModel>>? AdvancedProperties { get; }

    public IDevice? Device { get; }
    public ICommand ShowAdvancedCommand { get; }
    
    public DeviceSettingsViewModel() { }
    
    public bool HasAdvancedProperties => AdvancedProperties?.Count > 0;

    public DeviceSettingsViewModel(IDevice device)
    {
        Device = device;
        var properties = device.GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<GroupAttribute>() != null)
            .Select(p => new DevicePropertyViewModel(device, p))
            .ToList();

        BasicProperties = properties.Where(p => p.Group == "Basic").ToList();
        AdvancedProperties = properties.Where(p => p.Group != "Basic")
            .GroupBy(p => p.Group)
            .ToDictionary(g => g.Key, g => g.ToList());
        ShowAdvancedCommand = new RelayCommand(ShowAdvancedWindow);
    }
    private void ShowAdvancedWindow(object parameter)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = new AdvancedDeviceSettingsView
            {
                DataContext = this
            };
            window.Show();
        });
    }
}