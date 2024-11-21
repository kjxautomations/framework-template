using System.Collections.Generic;
using Framework.Core.ViewModels;
using Framework.Services;
using ProjectTemplate.Models;
using ProjectTemplate.Services;

namespace ProjectTemplate.ViewModels;

public class WelcomeScreenViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    public WelcomeScreenViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService, IEnumerable<NavigationStates> statesShowingView) 
        : base(navigationService, new [] { NavigationStates.Welcome })
    {
    }
}