namespace KJX.Devices;

/// <summary>
/// Interface for a simple motor.
/// </summary>
public interface IMotor : IDevice
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
    
    /// <summary>
    /// The minimum amount a motor can move. For a stepper, this mmight be the microstepping resolution,
    /// translated to the native units. For a servo, this will be the accuracy of the position holding
    /// loop.
    /// </summary>
    public double MinimumPositionIncrement { get;  }
}

/// <summary>
/// Interface for a simple stepper motor. Extended from IMotor.
/// </summary>
public interface IStepperMotor : IMotor
{
    /// <summary>
    /// The number of steps required to go 1 "Unit" of distance.
    /// For example, if 255 steps were required to go 1 mm, put 255 here
    /// </summary>
    public uint StepsPerUnit { get; set; }
}