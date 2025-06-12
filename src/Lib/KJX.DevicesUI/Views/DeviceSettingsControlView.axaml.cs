using Avalonia.Controls;
using Avalonia.Interactivity;


namespace KJX.DevicesUI.Views;

public partial class DeviceSettingsControlView : UserControl
{
    public DeviceSettingsControlView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Avalonia doesn't seem to support the IsOpen property on the Popup control in XAML.
    /// </summary>
    private void Button_OnClick(object sender, RoutedEventArgs e)
    {
        AdvancedOptionsPopup.IsOpen = !AdvancedOptionsPopup.IsOpen;
    }
}
