using KJX.Core.ViewModels;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public required StateNavigationViewModel StateNavigationViewModel { get; set; }

    public MainWindowViewModel()
    {
    }

}