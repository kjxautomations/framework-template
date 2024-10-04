using System;
using System.Reactive.Linq;
using Framework.Core.ViewModels;
using Framework.Services;
using FrameworkSample.Models;
using FrameworkSample.Services;
using ReactiveUI;

namespace FrameworkSample.ViewModels;

public class GatherRunInfoViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    private readonly INavigationService<NavigationStates, NavigationTriggers> _navigationService;

    public RunInfo RunInfo { get; }

    public GatherRunInfoViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService,
        RunInfo runInfo)
        : base(navigationService, new[] { NavigationStates.GatheringRunInfo })
    {
        _navigationService = navigationService;
        RunInfo = runInfo;
    }
}