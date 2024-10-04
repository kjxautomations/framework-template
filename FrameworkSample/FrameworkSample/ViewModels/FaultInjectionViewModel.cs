using System;
using System.Collections.ObjectModel;
using Framework.Core.ViewModels;
using DevicesUI.ViewModels;
using Framework.Devices;

namespace FrameworkSample.ViewModels;

public class FaultInjectionViewModel : ViewModelBase
{
    public ObservableCollection<SensorFaultInjectionViewModel> FaultInjectionViewModels { get; init; } = new();
    
    public FaultInjectionViewModel(ISensor[] sensors)
    {
        foreach (var sensor in sensors)
        {
            FaultInjectionViewModels.Add(new SensorFaultInjectionViewModel(sensor));
        }
    }
}