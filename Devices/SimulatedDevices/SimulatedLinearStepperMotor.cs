using System.ComponentModel;
using System.Runtime.CompilerServices;
using Framework.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace Framework.Devices;

public class SimulatedLinearStepperMotor : StepperMotorBase, ISupportsHoming
{
    public required ILogger<SimulatedLinearStepperMotor> Logger { get; init; }

    public SimulatedLinearStepperMotor(string name = "") : base(name)
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

    public double HomeOffset { get; set; }
}