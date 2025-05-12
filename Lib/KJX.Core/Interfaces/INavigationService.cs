using System.Collections.ObjectModel;
using System.ComponentModel;
using KJX.Core.Models;

namespace KJX.Core.Interfaces;

/// <summary>
/// Interface for a state-based navigation service.
/// </summary>
/// <typeparam name="TState"></typeparam>
/// <typeparam name="TTrigger"></typeparam>
public interface INavigationService<TState, TTrigger> : INotifyPropertyChanged
    where TState : System.Enum 
    where TTrigger : System.Enum
{
    public ObservableCollection<NavigationStateInfo<TState>> BreadcrumbStates { get; }
    public ObservableCollection<NavigationTriggerInfo<TTrigger>> ButtonTriggers { get; }
    
    public TState CurrentState { get; }

    public Task SendTrigger(TTrigger trigger);
}