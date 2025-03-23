using KJX.Config;

namespace KJX.Devices.Logic;

public abstract class MotorBase : DeviceBase, IMotor
{
    public string Units { get; protected set; }
    public string Name { get; set; }

    public double Position
    {
        get => _position;
        protected set => SetField(ref _position, value);
    }

    double _acceleration;
    [RangeIncrement(0, 100, 1)]
    [Group("Basic")]
    public required double Acceleration
    {
        get => _acceleration;
        set
        {
            if (value <= 0 || double.IsNaN(value))
                throw new ArgumentException("Value must be greater than 0");
            SetField(ref _acceleration, value);
        }
    }

    private double _velocity;
    [RangeIncrement(0, 100, 1)]
    [Group("Basic")]
    public required double Velocity
    {
        get => _velocity;
        set
        {
            if (value <= 0 || double.IsNaN(value))
                throw new ArgumentException("Value must be greater than 0");
            SetField(ref _velocity, value);
        }
    }
    public double? LowerLimit
    {
        get => _lowerLimit;
        set
        {
            if (Nullable.Equals(value, _lowerLimit)) return;
            SetField(ref _lowerLimit, value);
        }
    }

    public double? UpperLimit
    {
        get => _upperLimit;
        set
        {
            if (Nullable.Equals(value, _upperLimit)) return;
            SetField(ref _upperLimit, value);
        }
    }
    public bool EnforceLimits
    {
        get => _enforceLimits;
        set => SetField(ref _enforceLimits, value);
    }
    
    private double _position;
    private double? _lowerLimit;
    private double? _upperLimit;
    private bool _enforceLimits;
    
    public abstract void MoveTo(double newPosition);

    public abstract double MinimumPositionIncrement { get; }
}

public abstract class MotorBaseSupportsHoming : MotorBase, ISupportsHoming
{
    public bool IsHomed
    {
        get => _isHomed;
        private set => SetField(ref _isHomed, value);
    }
    [Group("Advanced")]
    public double HomeOffset
    {
        get => _homeOffset;
        set => SetField(ref _homeOffset, value);
    }

    public void Home()
    {
        DoHome();
        IsHomed = true;
        Position = 0;
        MoveTo(HomeOffset);
        Position = 0;
    }
    
    // concrete classes must implement this method
    protected abstract void DoHome();

    private bool _isHomed;
    private double _homeOffset;

}

public abstract class StepperMotorBase : MotorBase, IStepperMotor
{
    public StepperMotorBase(string name = "")
    {
        Name = name;
    }
    
    private uint _stepsPerUnit;
    
    public uint StepsPerUnit
    {
        get => _stepsPerUnit;
        set => SetField(ref _stepsPerUnit, value);
    }

    public override void MoveTo(double newPosition)
    {
        if (EnforceLimits)
        {
            if (LowerLimit.HasValue && newPosition < LowerLimit.Value)
                throw new ArgumentOutOfRangeException("newPosition", "Position is below lower limit");
            if (UpperLimit.HasValue && newPosition > UpperLimit.Value)
                throw new ArgumentOutOfRangeException("newPosition", "Position is above upper limit");
        }
        var delta = newPosition - Position;
        var deltaSteps = (int)(delta * StepsPerUnit);
        DoMoveSteps(deltaSteps);
        Position = newPosition;
    }

    /// <summary>
    /// For a stepper motor, the minimum position increment is the inverse of the steps per unit
    /// i.e. if 200 steps are required to move 1 mm, the minimum position increment is 1/200 mm
    /// </summary>
    public override double MinimumPositionIncrement => 1.0 / _stepsPerUnit;

    protected abstract void DoMoveSteps(int numberOfSteps);
}