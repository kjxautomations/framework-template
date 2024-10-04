namespace Framework.Devices.Logic;

public abstract class MotorBase : SupportsInitialization, IMotor
{
    public string Units => "mm";
    public string Name { get; set; }

    public double Position
    {
        get => _position;
        protected set => SetField(ref _position, value);
    }

    double _acceleration;
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
    private double _position;
    private double? _lowerLimit;
    private double? _upperLimit;
    
    public abstract void MoveTo(double newPosition);
}

public abstract class MotorBaseSupportsHoming : MotorBase, ISupportsHoming
{
    public bool IsHomed
    {
        get => _isHomed;
        private set => SetField(ref _isHomed, value);
    }
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
    private uint _microsteppingMode;

    public uint StepsPerUnit
    {
        get => _stepsPerUnit;
        set => SetField(ref _stepsPerUnit, value);
    }

    public uint MicrosteppingMode
    {
        get => _microsteppingMode;
        set => SetField(ref _microsteppingMode, value);
    }

    public override void MoveTo(double newPosition)
    {
        var delta = newPosition - Position;
        var deltaSteps = (int)(delta * StepsPerUnit);
        DoMoveSteps(deltaSteps);
        Position = newPosition;
    }

    protected abstract void DoMoveSteps(int numberOfSteps);
}