using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac;
using Avalonia;
using KJX.Config;
using KJX.Core;
using KJX.Core.ViewModels;
using KJX.ProjectTemplate.Engineering.Models;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.Engineering.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigurationHandler _configHandler;
    [Reactive] public bool IsPaneOpen { get; set; } = true;
    [Reactive] public string PaneOpenCloseIcon { get; set; } = "+";
    [Reactive] public ViewModelBase CurrentPage { get; set; } = new WelcomeScreenViewModel();
    [Reactive] public NavigationMenuType SelectedPage { get; set; }

    public ReactiveCommand<Unit, Unit> SaveConfigCommand { get; }

    public ObservableCollection<NavigationMenuType> NavigableMenuTypes { get; } =
    [
        new NavigationMenuType(typeof(WelcomeScreenViewModel)) { MenuLabel = "Welcome Screen" },
        new NavigationMenuType(typeof(DeviceShowcaseViewModel)) { MenuLabel = "Devices Showcase" }
    ];

    public MainWindowViewModel()
    {
        // just for the UI
    }

    public MainWindowViewModel(ConfigurationHandler configHandler)
    {
        _configHandler = configHandler;
        this.WhenAnyValue(x => x.SelectedPage).Subscribe(MenuItemSelection);
        var enableSave = Observable.CombineLatest(
                configHandler.WhenAnyValue(x => x.HasDirtyValues),
                configHandler.WhenAnyValue(x => x.HasObjectsThatDoNotImplementINotifyPropertyChanged))
            .Select(values => values.Any(value => value));

        SaveConfigCommand = ReactiveCommand.CreateFromTask(DoSaveConfig, enableSave);
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

    private async Task DoSaveConfig()
    {
        var path = (Application.Current as App).ConfigPath;
        string result;
        using (var stm = new StreamReader(path))
        {
            result = ConfigWriter.SaveEditedConfig(stm, _configHandler.GetChangedValues());
        }

        await MessageBoxManager.GetMessageBoxStandard("New Config",
            result, ButtonEnum.Ok, Icon.Success).ShowWindowAsync();

    }
}
