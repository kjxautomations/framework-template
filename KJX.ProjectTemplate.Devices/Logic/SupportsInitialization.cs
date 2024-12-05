using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KJX.ProjectTemplate.Devices.Logic;

public abstract class SupportsInitialization : ISupportsInitialization, INotifyPropertyChanged
{
    private bool _isInitialized;

    public bool IsInitialized
    {
        get => _isInitialized;
        protected set => SetField(ref _isInitialized, value);
    }

    public void Initialize()
    {
        if (IsInitialized) return;
        DoInitialize();
        IsInitialized = true;
    }

    public void Shutdown()
    {
        if (IsInitialized)
        {
            DoShutdown();
            IsInitialized = false;
        }
    }
    

    public ushort InitializationGroup { get; protected set; } = ushort.MaxValue;

    // derived classes implement these methods to perform initialization and (optional) shutdown
    protected abstract void DoInitialize();

    protected virtual void DoShutdown()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
