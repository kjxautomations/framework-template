using Autofac;
using Avalonia;
using KJX.ProjectTemplate.Core.ViewModels;

namespace KJX.ProjectTemplate.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public required StateNavigationViewModel StateNavigationViewModel { get; set; }

    public MainWindowViewModel()
    {
    }

}