using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using KJX.ProjectTemplate.Control.Models;
using KJX.ProjectTemplate.Core;
using KJX.ProjectTemplate.Devices;
using ReactiveUI;

namespace KJX.ProjectTemplate.Control.Services;

public enum NavigationStates
{
    Default,
    Initial
}

public enum NavigationTriggers
{
    Next,
    Previous,
    Cancel
}

public class NavigationService : INavigationService<NavigationStates, NavigationTriggers>
{
    public ObservableCollection<NavigationStateInfo<NavigationStates>> BreadcrumbStates { get; } =
    [
        new NavigationStateInfo<NavigationStates>(NavigationStates.Initial, "Initial") { IsActive = true }
    ];

    public ObservableCollection<NavigationTriggerInfo<NavigationTriggers>> ButtonTriggers { get; } = new()
    {
        new NavigationTriggerInfo<NavigationTriggers>(NavigationTriggers.Previous, "Previous"),
        new NavigationTriggerInfo<NavigationTriggers>(NavigationTriggers.Next, "Next") { IsEnabled = true },
        new NavigationTriggerInfo<NavigationTriggers>(NavigationTriggers.Cancel, "Cancel")
    };

    private NavigationStates _currentState;
    public NavigationStates CurrentState
    {
        get => _currentState;
        private set => SetField(ref _currentState, value);
    }

    public async Task SendTrigger(NavigationTriggers trigger)
    {
        await _stateMachine.SendTrigger(trigger);
    }

    Dictionary<NavigationStates, Dictionary<NavigationTriggers, bool>> _triggersEnabled = new();

    private void UpdateTriggerEnabled(NavigationStates state, NavigationTriggers trigger, bool enabled)
    {
        if (!_triggersEnabled.ContainsKey(state))
            _triggersEnabled[state] = new Dictionary<NavigationTriggers, bool>();
        _triggersEnabled[state][trigger] = enabled;
        if (state == _stateMachine.CurrentState)
        {
            foreach (var button in ButtonTriggers)
            {
                if (button.TriggerValue == trigger)
                    button.IsEnabled = enabled && _stateMachine.IsTriggerAllowed(trigger);
            }
        }
    }

    private readonly StateMachine _stateMachine;
   
    public NavigationService(StateMachine stateMachine, 
        ISupportsInitialization[] initializables)
    {
        _stateMachine = stateMachine;
        Action<NavigationStates> handler = HandleStateChange;
        _stateMachine.WhenAnyValue(s => s.CurrentState)
            .Subscribe(handler);
        
        // set up the navigation button enabled/disabled status
        
        // enable the Next button only when all have been initialized
        var needsInitialization = initializables
            .Select(item => item.WhenAnyValue(x => x.IsInitialized))
            .CombineLatest()
            .Select(isInitializedArray => isInitializedArray.Any(isInitialized => !isInitialized));
        needsInitialization.Subscribe(anyNotInitialized => UpdateTriggerEnabled(NavigationStates.Initial, NavigationTriggers.Next, !anyNotInitialized));
    }

    
    private void HandleStateChange(NavigationStates newState)
    {
        CurrentState = newState;
        // the breadcrumb states represent a linearization of the state machine
        // so, each one before newState is "active" in this metaphor
        bool isActive = true;
        foreach (var crumb in BreadcrumbStates)
        {
            crumb.IsActive = isActive;
            if (crumb.StateValue == newState)
            {
                foreach(var trigger in ButtonTriggers)
                    trigger.IsEnabled = _stateMachine.IsTriggerAllowed(trigger.TriggerValue);
                if (isActive)
                    isActive = false; // inactive after this state
            }
        }
        // reset the buttons to their default state
        foreach (var button in ButtonTriggers)
        {
            button.IsEnabled = _stateMachine.IsTriggerAllowed(button.TriggerValue);
        }
        // override the default if the viewmodels override the default
        if (_triggersEnabled.ContainsKey(newState))
        {
            foreach (var kvp in _triggersEnabled[newState])
            {
                foreach (var button in ButtonTriggers)
                {
                    if (button.TriggerValue == kvp.Key)
                        button.IsEnabled = kvp.Value && _stateMachine.IsTriggerAllowed(kvp.Key);
                }
            }
        }
        
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

public class NavigationMenuType
{
    public NavigationMenuType(Type type)
    {
        ViewModel = type;
    }
    
    public string MenuLabel { get; set; }
    public Type ViewModel { get; }
}