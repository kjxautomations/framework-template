namespace KJX.Devices;

/// <summary>
/// Simple LED interface.
/// Simple LED's are either on or off, hence the Enabled boolean in this scenario. 
/// </summary>
public interface ILed
{
    bool Enabled { get; set; }
}