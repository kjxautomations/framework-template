using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Framework.Devices;
using Framework.Services;
using FrameworkSample.Models;
using ReactiveUI;

namespace FrameworkSample.Services;

public enum NavigationStates
{
    Default,
    Welcome,
    Initial,
    GatheringRunInfo,
    ReadyToSequence,
    Sequencing,
    SequencingComplete,
    SequencingAborted,
    Washing,
    WashingComplete,
    WashingAborted,
}

public enum NavigationTriggers
{
    Next,
    Previous,
    Cancel,
    Abort
}

public class NavigationService : INavigationService<NavigationStates, NavigationTriggers>
{
    public ObservableCollection<NavigationStateInfo<NavigationStates>> BreadcrumbStates { get; } =
    [
        new NavigationStateInfo<NavigationStates>(NavigationStates.Welcome, "Welcome") { IsActive = true },
        new NavigationStateInfo<NavigationStates>(NavigationStates.Initial, "Initial"),
        new NavigationStateInfo<NavigationStates>(NavigationStates.GatheringRunInfo, "Gathering Run Info"),
        new NavigationStateInfo<NavigationStates>(NavigationStates.ReadyToSequence, "Ready to Sequence"),
        new NavigationStateInfo<NavigationStates>(NavigationStates.Sequencing, "Sequencing"),
        new NavigationStateInfo<NavigationStates>(NavigationStates.SequencingComplete, "Sequencing Complete"),
        new NavigationStateInfo<NavigationStates>(NavigationStates.Washing, "Washing"),
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
    private readonly RunInfo _runInfo;

    public NavigationService(StateMachine stateMachine, 
        RunInfo runInfo,
        ISupportsInitialization[] initializables,
        SequencingService sequencingService)
    {
        _stateMachine = stateMachine;
        _runInfo = runInfo;
        Action<NavigationStates> handler = HandleStateChange;
        _stateMachine.WhenAnyValue(s => s.CurrentState)
            .Subscribe(handler);
        
        // set up the navigation button enabled/disabled status
        _runInfo.WhenAnyValue(x => x.CyclesError, x => x.LanesError)
            .Select(x => string.IsNullOrEmpty(x.Item1) && string.IsNullOrEmpty(x.Item2))
            .Subscribe(enabled => UpdateTriggerEnabled(NavigationStates.GatheringRunInfo, NavigationTriggers.Next, enabled));
        
        // enable the Next button only when all have been initialized
        var needsInitialization = initializables
            .Select(item => item.WhenAnyValue(x => x.IsInitialized))
            .CombineLatest()
            .Select(isInitializedArray => isInitializedArray.Any(isInitialized => !isInitialized));
        needsInitialization.Subscribe(anyNotInitialized => UpdateTriggerEnabled(NavigationStates.Initial, NavigationTriggers.Next, !anyNotInitialized));
        // sequencing screen
        sequencingService.WhenAnyValue(x => x.State)
            .Subscribe(s =>
            {
                foreach (var state in new[] { NavigationStates.Sequencing, NavigationStates.ReadyToSequence })
                {
                    UpdateTriggerEnabled(state, NavigationTriggers.Next,
                        !OperatingSystem.IsBrowser() && (s == SequencingState.Complete || s == SequencingState.Idle));
                }
            });
        // the finish screen
        this.WhenAnyValue(x => x.CurrentState)
            .Subscribe((s) =>
            {
                if (s == NavigationStates.SequencingComplete || s == NavigationStates.SequencingAborted)
                {
                    UpdateTriggerEnabled(s, NavigationTriggers.Next,
                        sequencingService.State is SequencingState.Complete or SequencingState.Cancelled);
                } 
            });
        sequencingService.WhenAnyValue(x => x.State)
            .Subscribe(s =>
            {
                if (CurrentState == NavigationStates.SequencingComplete || CurrentState == NavigationStates.SequencingAborted)
                {
                    UpdateTriggerEnabled(CurrentState, NavigationTriggers.Next, 
                        s is SequencingState.Complete or SequencingState.Cancelled);
                }
            });
        
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