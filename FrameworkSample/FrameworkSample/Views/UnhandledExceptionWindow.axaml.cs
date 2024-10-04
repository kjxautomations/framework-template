using Avalonia;
using Avalonia.Controls;

namespace FrameworkSample.Views;

public partial class UnhandledExceptionWindow : Window
{
    public UnhandledExceptionWindow()
    {
        InitializeComponent();
        ExceptionsControl.Close += () => Close();
    }
   
}