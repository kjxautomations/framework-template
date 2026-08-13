using KJX.Scripting;

namespace KJX.Devices;

/// <summary>
/// Interface for devices that support and/or require initialization.
/// </summary>
public interface ISupportsInitialization
{
    public bool IsInitialized { get; }
    public void Initialize();
    public void Shutdown();
    /// <summary>
    /// Devices are initialized in groups, from 0-ushort.MaxValue, in parallel within groups
    /// </summary>
    public ushort InitializationGroup { get;  } 
}

/// <summary>
/// Typical of stages and motors, an interface for devices that require a knowledge of their origin.
/// </summary>
/// <remarks>
/// A capability rather than a kind of device: a driver mixes it in alongside IMotor, and the
/// configuration decides which instances expose it. Scripts reach it because a target carries the
/// set of interfaces it was registered under, not a single type.
/// </remarks>
[ScriptApi]
public interface ISupportsHoming
{
    public bool IsHomed { get; }
    /// <summary>
    /// Executes the home routine, moves the home offset, and resets the position to 0
    /// </summary>
    public void Home();
    
    /// <summary>
    /// After the hardware-level home routine, move this amount and reset Position to 0
    /// </summary>
    public double HomeOffset { get; set; }

}