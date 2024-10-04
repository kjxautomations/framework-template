using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FrameworkSample.Views;

public partial class UnhandledExceptionControl : UserControl
{
    public UnhandledExceptionControl()
    {
        InitializeComponent();
    }
    public event Action Close;
     
    private void Close_onClick(object? sender, RoutedEventArgs e)
    {
        Close?.Invoke();
    }
}