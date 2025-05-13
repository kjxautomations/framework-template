using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Avalonia;
using KJX.Core.Interfaces;
using KJX.Core.Models;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Control.Services;
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
#if (!AsTemplate)
    private readonly SequencingService _sequencingService; 
    
    public StateMachine(SequencingService sequencingService, ILogger<StateMachine> logger,
        INotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
        _sequencingService = sequencingService;
        
        _sm = new StateMachine<NavigationStates, NavigationTriggers>(() => CurrentState,
            (s) => CurrentState = s);
        this.WhenAnyValue(x => x.CurrentState)
            .Subscribe(s => _logger.LogDebug("Current state is {0}", s));
        AddTransitions();
        AddPerStateHandlers();
        _sm.Activate();
    }
#elif (AsTemplate)
    public StateMachine(ILogger<StateMachine> logger, INotificationService notificationService) {
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
#endif

    private void AddTransitions()
    {
        _sm.Configure(NavigationStates.Default)
            .Permit(NavigationTriggers.Next, NavigationStates.Welcome);
#if (AsTemplate)
        //Configure navigation from Welcome Screen
        _sm.Configure(NavigationStates.Welcome)
            .Permit(NavigationTriggers.Previous, NavigationStates.Default);
#elif (!AsTemplate)
        //Configure navigation from Welcome Screen
        _sm.Configure(NavigationStates.Welcome)
            .Permit(NavigationTriggers.Next, NavigationStates.Initialize);
        
        //Configure navigation from Initialization screen
        _sm.Configure(NavigationStates.Initialize)
            .Permit(NavigationTriggers.Previous, NavigationStates.Welcome);
        _sm.Configure(NavigationStates.Initialize)
            .Permit(NavigationTriggers.Next, NavigationStates.GatherRunInfo);
        
        //Configure navigation from GatherRunInfo screen
        _sm.Configure(NavigationStates.GatherRunInfo)
            .Permit(NavigationTriggers.Previous, NavigationStates.Initialize);
        _sm.Configure(NavigationStates.GatherRunInfo)
            .Permit(NavigationTriggers.Next, NavigationStates.ReadyToSequence);
        
        //Configure navigation for Sequencing screens
        _sm.Configure(NavigationStates.ReadyToSequence)
            .Permit(NavigationTriggers.Previous, NavigationStates.GatherRunInfo);
        _sm.Configure(NavigationStates.ReadyToSequence)
            .Permit(NavigationTriggers.Next, NavigationStates.Sequencing);
        
        _sm.Configure(NavigationStates.Sequencing)
            .Permit(NavigationTriggers.Next, NavigationStates.SequencingComplete);
        _sm.Configure(NavigationStates.Sequencing)
            .Permit(NavigationTriggers.Cancel, NavigationStates.SequencingComplete);
        _sm.Configure(NavigationStates.Sequencing)
            .Permit(NavigationTriggers.Abort, NavigationStates.SequencingAborted);
        
        _sm.Configure(NavigationStates.SequencingComplete)
            .Permit(NavigationTriggers.Next, NavigationStates.Washing);
        _sm.Configure(NavigationStates.SequencingAborted)
            .Permit(NavigationTriggers.Next, NavigationStates.Washing);
        
        //Configure navigation for Washing screens
        _sm.Configure(NavigationStates.Washing)
            .Permit(NavigationTriggers.Next, NavigationStates.WashingComplete);
        _sm.Configure(NavigationStates.Washing)
            .Permit(NavigationTriggers.Cancel, NavigationStates.WashingComplete);
        _sm.Configure(NavigationStates.Washing)
            .Permit(NavigationTriggers.Abort, NavigationStates.WashingAborted);
        _sm.Configure(NavigationStates.WashingComplete)
            .Permit(NavigationTriggers.Next, NavigationStates.Welcome);
#endif
}

#if (!AsTemplate)
    async Task StartOrCancelSequencing(Stateless.StateMachine<NavigationStates, NavigationTriggers>.Transition transition)
    {
        // when we transition from ReadyToSequence to Sequencing, start the sequencing
        if (transition.Destination == NavigationStates.Sequencing)
        {
            _sequencingService.StartSequencing();
        }
        // cancellation
        else if (transition.Destination == NavigationStates.SequencingComplete &&
                 transition.Trigger == NavigationTriggers.Cancel)
        {
            _notificationService.AddNotification(NotificationType.Info, "Sequencing was cancelled");
            await Task.Run(() => _sequencingService.CancelSequencing());
        }
        // abort handling
        else if (transition.Destination == NavigationStates.SequencingAborted)
        {
            _notificationService.AddNotification(NotificationType.Info, "Sequencing was aborted");
            await Task.Run(() => _sequencingService.CancelSequencing());
        }
    } 
#endif

    private void AddPerStateHandlers()
    {
#if (!AsTemplate)
        // reset the RunInfo and all views to their initial state after the first time we enter the Initial state
        this.WhenAnyValue(s => s.CurrentState)
            .Skip(1)
            .Where(s => s == NavigationStates.Initialize)
            .Subscribe(_ =>
            {
                var container = (Application.Current as App)?.Container;
                var runInfo = container.Resolve<RunInfo>();
                runInfo.Reset();
                // get all types from the current assembly that are subclasses of StateViewModelBase
                var types = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(StateViewModelBase<NavigationStates,NavigationTriggers>)));
                
                foreach (var type in types)
                {
                    var component = container.Resolve(type) as StateViewModelBase<NavigationStates,NavigationTriggers>;
                    component?.Reset();
                }
                _sequencingService.Reset();
            });
        // when the sequencing service is done, transition to the next state
        _sequencingService.WhenAnyValue(x => x.State)
            .Skip(1)
            .Where(s => s == SequencingState.Complete)
            .Subscribe(async _ =>
            {
                await _sm.FireAsync(NavigationTriggers.Next);
            });

        // for actually controlling the sequencing, we have to use an async method because cancelling is blocking
        _sm.OnTransitionedAsync(StartOrCancelSequencing);
#endif
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