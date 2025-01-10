namespace KJX.Devices;

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