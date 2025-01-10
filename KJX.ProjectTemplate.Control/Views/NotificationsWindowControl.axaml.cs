using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KJX.Core.Interfaces;

namespace KJX.ProjectTemplate.Control.Views;

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