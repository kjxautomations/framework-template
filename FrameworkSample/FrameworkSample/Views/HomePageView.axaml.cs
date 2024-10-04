using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace FrameworkSample.Views;

public partial class HomePageView : UserControl
{
    public HomePageView()
    {
        InitializeComponent();
        var themeVariants = this.Get<ComboBox>("ThemeVariants");
        themeVariants.SelectedItem = Application.Current!.RequestedThemeVariant;
        themeVariants.SelectionChanged += (sender, e) =>
        {
            if (themeVariants.SelectedItem is ThemeVariant themeVariant)
            {
                Application.Current!.RequestedThemeVariant = themeVariant;
            }
        };
    }
}