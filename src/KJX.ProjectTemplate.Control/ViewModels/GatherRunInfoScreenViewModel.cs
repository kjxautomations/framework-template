using System.Collections.Generic;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Control.Models;
using KJX.ProjectTemplate.Control.Services;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class GatherRunInfoScreenViewModel : StateViewModelBase<NavigationStates, NavigationTriggers>
{
    private readonly INavigationService<NavigationStates, NavigationTriggers> _navigationService;
    public RunInfo RunInfo { get; }
    
    public GatherRunInfoScreenViewModel(
        INavigationService<NavigationStates, NavigationTriggers> navigationService, IEnumerable<NavigationStates> statesShowingView,
        RunInfo runInfo) 
        : base(navigationService, [NavigationStates.GatherRunInfo])
    {
        _navigationService = navigationService;
        RunInfo = runInfo;
    }
}