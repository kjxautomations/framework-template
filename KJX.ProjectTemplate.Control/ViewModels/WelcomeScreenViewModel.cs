using System.Collections.Generic;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Control.Services;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class WelcomeScreenViewModel : StateViewModelBase<NavigationStates, NavigationTriggers>
{
    public string Message { get; set; } = "Welcome to the Control Workflow Demo";
    
    public WelcomeScreenViewModel(
        INavigationService<NavigationStates, NavigationTriggers> navigationService, IEnumerable<NavigationStates> statesShowingView) 
        : base(navigationService, [NavigationStates.Welcome])
    {
    }
}