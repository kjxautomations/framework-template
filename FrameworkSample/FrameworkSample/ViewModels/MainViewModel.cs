using System;
using System.Collections.ObjectModel;
using Avalonia;
using Autofac;
using Framework.Core.ViewModels;
using FrameworkSample.Models;
using FrameworkSample.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace FrameworkSample.ViewModels;

public class MainViewModel : ViewModelBase
{
    [Reactive] public bool IsPaneOpen { get; set; } = true;
    [Reactive] public string PaneOpenCloseIcon { get; set; } = "+";
    [Reactive] public ViewModelBase CurrentPage { get; set; } = new HomePageViewModel();
    [Reactive] public NavigationMenuType SelectedPage { get; set; }

    public ObservableCollection<NavigationMenuType> NavigationStateViewsList { get; } =
    [
        new NavigationMenuType(typeof(HomePageViewModel)) { MenuLabel = "Home Page" },
        new NavigationMenuType(typeof(DeviceShowcaseViewModel)) { MenuLabel = "Device Showcase" },
        new NavigationMenuType(typeof(FaultInjectionViewModel)) { MenuLabel = "Fault Injection" },
        new NavigationMenuType(typeof(StateNavigationViewModel)) { MenuLabel = "State Machine Navigation Demo" }
    ];

    public MainViewModel()
    {
        this.WhenAnyValue(x => x.SelectedPage).Subscribe(MenuItemSelection);
    }

    public void OpenClosePaneCommand()
    {
        IsPaneOpen = !IsPaneOpen;
        PaneOpenCloseIcon = IsPaneOpen ? "-" : "+";
    }

    private void MenuItemSelection(NavigationMenuType? value)
    {
        if (value is null) return;
        var instance = (Application.Current as App).Container.Resolve(value.ViewModel);
        if (instance is null) return;

        CurrentPage = (ViewModelBase)instance;
    }
}