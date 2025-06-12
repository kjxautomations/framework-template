using System;
using System.Collections.Generic;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Control.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class WashingScreenViewModel : StateViewModelBase<NavigationStates, NavigationTriggers>
{
    [Reactive] public string StatusMessage { get; set; } = "Washing";

    public WashingScreenViewModel(
        INavigationService<NavigationStates, NavigationTriggers> navigationService, IEnumerable<NavigationTriggers> statesShowingView) 
        : base(navigationService, [NavigationStates.Washing, NavigationStates.WashingComplete])
    {
        var callback = new Action<NavigationStates>(state =>
        {
            if (state == NavigationStates.Washing)
                StatusMessage = "Washing";
            else if (state == NavigationStates.WashingComplete)
                StatusMessage = "Washing Complete";
            else
                StatusMessage = "Washing Failed";
            
        });
        
        //could create a wash service to handle this more cleanly, like we do with sequencing. decided to keep it simple
        this.WhenAnyValue(x => x.CurrentState).Subscribe(callback);
    }
}