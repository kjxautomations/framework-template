using System.ComponentModel;
using KJX.Scripting;

namespace KJX.Devices;

/// <summary>
/// The base of every device. Scriptable: the members of <see cref="ISupportsInitialization"/>
/// come with it, while <see cref="INotifyPropertyChanged"/> is infrastructure for the UI and is
/// not part of the scripting surface.
/// </summary>
[ScriptApi]
public interface IDevice : ISupportsInitialization, INotifyPropertyChanged
{
    /// <summary>
    /// True while the device is carrying out an operation.
    /// </summary>
    public bool IsBusy { get; }
}