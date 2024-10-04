using System;
using System.IO;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Framework.Core.ViewModels;
using ReactiveUI;

namespace FrameworkSample.ViewModels;

public class UnhandledExceptionViewModel : ViewModelBase
{
    public UnhandledExceptionViewModel(Exception theException)
    {
        TheException = theException;
        SaveText = ReactiveCommand.Create(DoSave);
    }
    public Exception TheException { get; }
    public ReactiveCommand<Unit, Unit> SaveText { get; }
  
    private void DoSave()
    {
        var desktop = Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;

        // Start async operation to open the dialog.
        var file = mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Text File"
        }).Result;

        if (file is not null)
        {
            // Open writing stream from the file.
            using var stream = file.OpenWriteAsync().Result;
            using var streamWriter = new StreamWriter(stream);
            streamWriter.Write(TheException.ToString());
        }
    }
}