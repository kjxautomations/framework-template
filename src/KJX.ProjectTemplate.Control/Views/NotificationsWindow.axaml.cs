using Avalonia.Controls;

namespace KJX.ProjectTemplate.Control.Views;

public partial class NotificationsWindow : Window
{
    public NotificationsWindow()
    {
        InitializeComponent();
        this.NotificationsControl.Close += () => Close();
    }
    
    
}