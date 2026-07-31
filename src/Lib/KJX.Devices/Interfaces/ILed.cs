using KJX.Scripting;

namespace KJX.Devices;

/// <summary>
/// Simple LED interface.
/// Simple LED's are either on or off, hence the Enabled boolean in this scenario.
/// </summary>
[ScriptApi]
public interface ILed : IDevice
{
    /// <summary>
    /// Whether the LED is lit.
    /// </summary>
    bool Enabled { get; set; }
}