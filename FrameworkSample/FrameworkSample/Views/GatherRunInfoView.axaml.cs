using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using FrameworkSample.ViewModels;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

namespace FrameworkSample.Views;

public partial class GatherRunInfoView : ReactiveUserControl<GatherRunInfoViewModel>
{
    public GatherRunInfoView()
    {
        InitializeComponent();
    }
}