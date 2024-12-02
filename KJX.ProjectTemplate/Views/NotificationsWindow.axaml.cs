using Avalonia.Controls;

namespace KJX.ProjectTemplate.Views;

public partial class NotificationsWindow : Window
{
    public NotificationsWindow()
    {
        InitializeComponent();
        this.NotificationsControl.Close += () => Close();
    }
    
    
}