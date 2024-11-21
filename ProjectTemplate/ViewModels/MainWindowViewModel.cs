using Autofac;
using Avalonia;
using Framework.Core.ViewModels;

namespace ProjectTemplate.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public StateNavigationViewModel StateNavigationViewModel { get; init; }
    public MainWindowViewModel()
    {
        (Application.Current as App).Container.Resolve(typeof(StateNavigationViewModel));
    }
    
}