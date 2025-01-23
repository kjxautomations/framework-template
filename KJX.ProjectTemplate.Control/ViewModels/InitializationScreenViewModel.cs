using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using KJX.ProjectTemplate.Control.Services;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.Devices;
using KJX.Devices.Logic;
using ReactiveUI;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class InitializationScreenViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    public ReactiveCommand<Unit, Unit> InitializeCommand { get; }

    public InitializationScreenViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService,
        ISupportsInitialization[] initializables, ISupportsHoming[] homables) 
        : base (navigationService, new [] {NavigationStates.Initialize})
    {
        var needsInitialization = initializables
            .Select(item => item.WhenAnyValue(x => x.IsInitialized))
            .CombineLatest()
            .Select(isInitializedArray => isInitializedArray.Any(isInitialized => !isInitialized));
       InitializeCommand = ReactiveCommand.Create(() => DoInitialization(initializables, homables), needsInitialization);
    }

    private void DoInitialization(ISupportsInitialization[] initializables, ISupportsHoming[] homables)
    {
        Initializer.Initialize(initializables);
        for (var i = 0; i < homables.Length; i++)
        {
            homables[i].Home();
        }
    }
}