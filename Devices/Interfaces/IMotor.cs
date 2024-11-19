namespace Framework.Devices;

public interface IMotor 
{
    public string Name { get; }
    // mostly for UI display, things like "mm" or "degrees"
    public string Units { get;  }
    /// <summary>
    ///  The position relative to home, or 0
    /// </summary>
    public double Position { get; }
    /// <summary>
    /// Move to the target position. Throws an exception if the position is outside the limits
    /// This routine is blocking, i.e. it returns when the stage stops moving
    /// </summary>
    /// <param name="newPosition"></param>
    public void MoveTo(double newPosition);
    public double Acceleration { get; set; }
    public double Velocity { get; set; }
    
    public double? LowerLimit { get; set; }
    public double? UpperLimit { get; set; }
    
    public bool EnforceLimits { get; set; }
}

public interface IStepperMotor : IMotor
{
    /// <summary>
    /// The number of steps required to go 1 "Unit" of distance.
    /// For example, if 255 steps were required to go 1 mm, put 255 here
    /// </summary>
    public uint StepsPerUnit { get; set; }
    /// <summary>
    /// Implementation-dependent value for setting microstepping. If using microstepping,
    /// StepsPerUnit must be adjusted accordingly
    /// </summary>
    public uint MicrosteppingMode { get; set; }

}