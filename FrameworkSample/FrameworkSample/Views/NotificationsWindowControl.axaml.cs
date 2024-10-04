using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Framework.Services;

namespace FrameworkSample.Views;

public partial class NotificationsWindowControl : UserControl
{
    public NotificationsWindowControl()
    {
        InitializeComponent();
        
    }
    private void DataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (NotificationRecord item in e.AddedItems)
        {
            item.HasBeenRead = true;
        }
    }
    
    public event Action Close;

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        Close?.Invoke();
    }
}