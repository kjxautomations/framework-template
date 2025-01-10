using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KJX.Core.ViewModels;

namespace KJX.Core;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var assembly = data.GetType().Assembly;
        // load the type from the assembly
        var type = assembly.GetType(name);

        if (type == null) return new TextBlock { Text = "Not Found: " + name };
        var control = (Control)Activator.CreateInstance(type)!;
        control.DataContext = data;
        return control;

    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}