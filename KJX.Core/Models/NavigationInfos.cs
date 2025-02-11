using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KJX.Core.Models;

public class NavigationStateInfo<TState>(TState state, string stateName) : INotifyPropertyChanged
    where TState : System.Enum
{
    public string StateName { get; init; } = stateName;
    public TState StateValue { get; init; } = state;
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
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
public class NavigationTriggerInfo<TTrigger>(TTrigger trigger, string triggerName) : INotifyPropertyChanged
    where TTrigger : System.Enum
{
    public string TriggerName { get; init; } = triggerName;
    public TTrigger TriggerValue { get; init; } = trigger;
    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
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