using System.ComponentModel;

namespace KJX.Devices;

public interface IDevice : ISupportsInitialization, INotifyPropertyChanged
{
    public bool IsBusy { get; }
}