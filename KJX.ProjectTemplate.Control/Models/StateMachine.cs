using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Avalonia;
using KJX.ProjectTemplate.Control.Services;
using KJX.ProjectTemplate.Core;
using KJX.ProjectTemplate.Core.ViewModels;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Stateless;

namespace KJX.ProjectTemplate.Control.Models;

public class StateMachine : ReactiveObject
{
    [Reactive]
    public NavigationStates CurrentState { get; set; } = NavigationStates.Default;
    
    private Stateless.StateMachine<NavigationStates, NavigationTriggers> _sm;
    private readonly ILogger<StateMachine> _logger;
    private readonly INotificationService _notificationService;

    public StateMachine(ILogger<StateMachine> logger,
        INotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
        
        _sm = new StateMachine<NavigationStates, NavigationTriggers>(() => CurrentState,
            (s) => CurrentState = s);
        this.WhenAnyValue(x => x.CurrentState)
            .Subscribe(s => _logger.LogDebug("Current state is {0}", s));
        AddTransitions();
        AddPerStateHandlers();
        _sm.Activate();
    }

    private void AddTransitions()
    {
        _sm.Configure(NavigationStates.Default)
            .Permit(NavigationTriggers.Next, NavigationStates.Initial);
        _sm.Configure(NavigationStates.Initial)
            .Permit(NavigationTriggers.Next, NavigationStates.End);
        _sm.Configure(NavigationStates.End)
            .Permit(NavigationTriggers.Previous, NavigationStates.Initial);
    }

    private void AddPerStateHandlers()
    {
        // reset all views to their initial state after the first time we enter the Initial state
        this.WhenAnyValue(s => s.CurrentState)
            .Skip(1)
            .Where(s => s == NavigationStates.Initial)
            .Subscribe(_ =>
            {
                var container = (Application.Current as App)?.Container;
                // get all types from the current assembly that are subclasses of StateViewModelBase
                var types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(StateViewModelBase<NavigationStates,NavigationTriggers>)));
                
                foreach (var type in types)
                {
                    var component = container.Resolve(type) as StateViewModelBase<NavigationStates,NavigationTriggers>;
                    component?.Reset();
                }
            });
    }

    public async Task SendTrigger(NavigationTriggers trigger)
    {
        await _sm.FireAsync(trigger);
    }

    public bool IsTriggerAllowed(NavigationTriggers triggerValue)
    {
        return _sm.CanFire(triggerValue);
    }
}