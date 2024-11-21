using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Framework.Services;

namespace ProjectTemplate.Views;

public partial class NotificationsWindow : Window
{
    public NotificationsWindow()
    {
        InitializeComponent();
        this.NotificationsControl.Close += () => Close();
    }
    
    
}