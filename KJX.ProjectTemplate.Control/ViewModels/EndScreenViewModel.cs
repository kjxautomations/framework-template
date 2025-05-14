using System.Collections.Generic;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Control.Services;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class EndScreenViewModel : StateViewModelBase<NavigationStates, NavigationTriggers>
{
    public string Message { get; set; } = "End of the Control Workflow Demo";

    public EndScreenViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService, 
        IEnumerable<NavigationStates> statesShowingView) 
        : base(navigationService, [NavigationStates.End])
    {
    }
}