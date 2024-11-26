using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Framework.Core.ViewModels;
using Framework.Services;
using KJX.ProjectTemplate.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.ViewModels;

public class StateNavigationViewModel : ViewModelBase
{
    [Reactive]
    public StateViewModelBase<NavigationStates,NavigationTriggers> CurrentPage { get; private set; }
    public required NotificationsViewModel Notifications { get; init; }
    public required INavigationService<NavigationStates, NavigationTriggers> NavigationService { get; init; }
    
    public ReactiveCommand<NavigationTriggers, Unit> SendTrigger { get; }

    private List<StateViewModelBase<NavigationStates,NavigationTriggers>> _stateViewModels;
    
    public StateNavigationViewModel(StateViewModelBase<NavigationStates,NavigationTriggers>[] viewModels)
    {
        _stateViewModels = viewModels.ToList();
        SendTrigger = ReactiveCommand.Create((NavigationTriggers trigger) =>
        {
            NavigationService.SendTrigger(trigger);
        });
        foreach (var vm in _stateViewModels)
        {
            var currentModel = vm;
            vm.WhenAnyValue(x => x.ViewVisible)
                .Subscribe(visible =>
                {
                    if (visible)
                    {
                        CurrentPage = currentModel;
                    }
                });
        }
        
    }
}