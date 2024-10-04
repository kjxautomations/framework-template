using System;
using System.Collections.Generic;
using Framework.Core.ViewModels;
using Framework.Services;
using FrameworkSample.Models;
using FrameworkSample.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace FrameworkSample.ViewModels;

public class WashingViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    [Reactive] public string StatusMessage { get; set; } = "Washing";

    public WashingViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService, IEnumerable<NavigationStates> statesShowingView) 
        : base(navigationService, new [] { NavigationStates.Washing , NavigationStates.WashingComplete })
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