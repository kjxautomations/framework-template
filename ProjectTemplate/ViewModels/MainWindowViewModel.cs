using Autofac;
using Avalonia;
using Framework.Core.ViewModels;

namespace ProjectTemplate.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public required StateNavigationViewModel StateNavigationViewModel { get; set; }

    public MainWindowViewModel()
    {
    }

}