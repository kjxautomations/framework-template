using KJX.Config;
using KJX.Devices.Logic;
using Microsoft.Extensions.Logging;

namespace KJX.Devices;

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
        IsBusy = true;
        Thread.Sleep(10);
        IsBusy = false;
    }

    private bool _isHomed;
    private double _homeOffset;
    private int _holdingCurrent;

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
    public double HomeOffset
    {
        get => _homeOffset;
        set => SetField(ref _homeOffset, value);
    }

    [RangeIncrement(0, 100, 1)]
    [Group("Advanced")]
    public int HoldingCurrent
    {
        get => _holdingCurrent;
        set => SetField(ref _holdingCurrent, value);
    }
}