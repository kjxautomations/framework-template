using System.ComponentModel;
using System.Runtime.CompilerServices;
using KJX.Config;
using KJX.Devices.Logic;
using KJX.Devices;
using Microsoft.Extensions.Logging;

namespace KJX.Devices;

public class LinearStepperMotor : StepperMotorBase, ISupportsHoming
{
    public required ILogger<SimulatedLinearStepperMotor> Logger { get; init; }

    public LinearStepperMotor(string name = "") : base(name)
    {
        Name = name;
    }
    
    protected override void DoInitialize()
    {
        Logger.LogInformation("Initializing");
        
    }

    protected override void DoMoveSteps(int numberOfSteps)
    {
    }

    private bool _isHomed;
    public bool IsHomed 
    {
        get => _isHomed;
        private set => SetField(ref _isHomed, value);
    }
    public void Home()
    {
        IsHomed = true;
        Position = 0;
    }

    [RangeIncrement(-10, 10, 0.1)]
    [Group("Advanced")]
    public double HomeOffset { get; set; }
    
    [RangeIncrement(0, 100, 1)]
    [Group("Advanced")]
    public int HoldingCurrent { get; set; }
}