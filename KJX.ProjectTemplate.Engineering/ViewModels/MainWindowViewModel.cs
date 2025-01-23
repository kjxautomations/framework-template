using System;
using System.Collections.ObjectModel;
using Autofac;
using Avalonia;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Engineering.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.Engineering.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    [Reactive] public bool IsPaneOpen { get; set; } = true;
    [Reactive] public string PaneOpenCloseIcon { get; set; } = "+";
    [Reactive] public ViewModelBase CurrentPage { get; set; } = new WelcomeScreenViewModel();
    [Reactive] public NavigationMenuType SelectedPage { get; set; }

    public ObservableCollection<NavigationMenuType> NavigableMenuTypes { get; } =
    [
        new NavigationMenuType(typeof(WelcomeScreenViewModel)) { MenuLabel = "Welcome Screen" },
        new NavigationMenuType(typeof(DeviceShowcaseViewModel)) { MenuLabel = "Devices Showcase" }
    ];
    
    public MainWindowViewModel()
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
