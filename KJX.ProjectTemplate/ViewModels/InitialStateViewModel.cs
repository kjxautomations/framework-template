using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Framework.Core.ViewModels;
using Framework.Services;
using KJX.ProjectTemplate.Devices;
using KJX.ProjectTemplate.Devices.Logic;
using KJX.ProjectTemplate.Services;
using ReactiveUI;

namespace KJX.ProjectTemplate.ViewModels;

public class InitialStateViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    public ReactiveCommand<Unit, Unit> InitializeCommand { get; }

    public InitialStateViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService,
        ISupportsInitialization[] initializables, ISupportsHoming[] homables) 
        : base (navigationService, new [] {NavigationStates.Initial})
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