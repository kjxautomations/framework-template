using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using KJX.Core.ViewModels;
using KJX.Devices;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Fody.Helpers;

namespace KJX.DevicesUI.ViewModels;

public class SimpleMotorControlViewModel : ViewModelBase
{
    private IMotor _motor;
    private readonly ISupportsHoming _homing;
    [Reactive] public string Name { get; set; }
    [Reactive] public double Position { get; set; }
    [Reactive] public double PositionToMoveTo { get; set; }
    [Reactive] public string Units { get; set; }
    [Reactive] public double? LowerLimit { get; set; }
    [Reactive] public double? UpperLimit { get; set; }
    [Reactive] public bool EnforceLimits { get; set; }

    /// <summary>
    /// True when the motor mixes in <see cref="ISupportsHoming"/>. The Home button is hidden for
    /// motors that do not, and those motors are never gated on being homed.
    /// </summary>
    public bool SupportsHoming => _homing != null;

    [Reactive] public bool IsHomed { get; private set; }

    /// <summary>
    /// Homing is supported but has not happened yet, so the motor's position means nothing.
    /// </summary>
    [Reactive] public bool NeedsHoming { get; private set; }

    /// <summary>
    /// True while a move or a home is running. Both operations feed the one flag, so neither can
    /// be started on top of the other.
    /// </summary>
    [Reactive] public bool IsBusy { get; private set; }

    /// <summary>
    /// Gates everything except the Home button: a motor that can home is locked out until it has
    /// been homed, and nothing can be touched while an operation is in progress.
    /// </summary>
    [Reactive] public bool CanOperate { get; private set; }

    public double MinimumPositionIncrement => _motor.MinimumPositionIncrement;
    public ReactiveCommand<double, Unit> MoveToPositionCommand { get; set; }
    public ReactiveCommand<Unit, Unit> HomeCommand { get; }

    public SimpleMotorControlViewModel(IMotor motor)
    {
        _motor = motor;
        _homing = motor as ISupportsHoming;
        Name = motor.Name;
        Units = _motor.Units;
        LowerLimit = _motor.LowerLimit;
        UpperLimit = _motor.UpperLimit;
        EnforceLimits = _motor.EnforceLimits;
        IsHomed = _homing != null && _homing.IsHomed;
        DeviceSettings = new DeviceSettingsViewModel(_motor);


        this.WhenAnyValue(x => x.LowerLimit).Subscribe(x => _motor.LowerLimit = x);
        this.WhenAnyValue(x => x.UpperLimit).Subscribe(x => _motor.UpperLimit = x);
        this.WhenAnyValue(x => x.EnforceLimits).Subscribe(x => _motor.EnforceLimits = x);


        _motor.WhenAnyValue(m => m.Position).BindTo(this, x => x.Position);

        // Home() runs on a worker thread, so its notification has to be brought back to the UI.
        _homing?.WhenAnyValue(h => h.IsHomed)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(homed => IsHomed = homed);

        // Both inputs are already on the UI thread, so this settles synchronously and the commands
        // below pick up the right initial state when they subscribe.
        this.WhenAnyValue(x => x.IsBusy, x => x.IsHomed)
            .Subscribe(_ =>
            {
                NeedsHoming = SupportsHoming && !IsHomed;
                CanOperate = !IsBusy && !NeedsHoming;
            });

        MoveToPositionCommand = ReactiveCommand.CreateFromTask<double>(MoveToPosition,
            this.WhenAnyValue(x => x.CanOperate));
        HomeCommand = ReactiveCommand.CreateFromTask(Home,
            this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy));

        MoveToPositionCommand.IsExecuting
            .CombineLatest(HomeCommand.IsExecuting, (moving, homing) => moving || homing)
            .Subscribe(busy => IsBusy = busy);
    }

    public DeviceSettingsViewModel DeviceSettings { get; }

    private Task MoveToPosition(double newPosition) =>
        RunOperation("Error Moving Motor to Position", () => _motor.MoveTo(newPosition));

    private Task Home() => RunOperation("Error Homing Motor", () => _homing.Home());

    private async Task RunOperation(string errorTitle, Action operation)
    {
        try
        {
            await Task.Run(operation);
        }
        catch (Exception e)
        {
            await Dispatcher.UIThread.Invoke(() =>
                MessageBoxManager.GetMessageBoxStandard(errorTitle,
                e.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync());
        }
    }
}
