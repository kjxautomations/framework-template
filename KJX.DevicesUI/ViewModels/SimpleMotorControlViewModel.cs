using System.Reactive;
using Avalonia.Threading;
using KJX.Core.ViewModels;
using KJX.Devices;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.DevicesUI.ViewModels;

public class SimpleMotorControlViewModel : ViewModelBase
{
    private IMotor _motor;
    [Reactive] public string Name { get; set; }
    [Reactive] public double Position { get; set; }
    [Reactive] public double PositionToMoveTo { get; set; }
    [Reactive] public string Units { get; set; }
    [Reactive] public double? LowerLimit { get; set; }
    [Reactive] public double? UpperLimit { get; set; }
    [Reactive] public bool EnforceLimits { get; set; }
    
    public double MinimumPositionIncrement => _motor.MinimumPositionIncrement;
    public ReactiveCommand<double, Unit> MoveToPositionCommand { get; set; }
    
    public SimpleMotorControlViewModel(IMotor motor)
    {
        _motor = motor;
        Name = motor.Name;
        Units = _motor.Units;
        LowerLimit = _motor.LowerLimit;
        UpperLimit = _motor.UpperLimit;
        EnforceLimits = _motor.EnforceLimits;
        DeviceSettings = new DeviceSettingsViewModel(_motor);

        
        this.WhenAnyValue(x => x.LowerLimit).Subscribe(x => _motor.LowerLimit = x);
        this.WhenAnyValue(x => x.UpperLimit).Subscribe(x => _motor.UpperLimit = x);
        this.WhenAnyValue(x => x.EnforceLimits).Subscribe(x => _motor.EnforceLimits = x);
        
        
        _motor.WhenAnyValue(m => m.Position).BindTo(this, x => x.Position);
        
        MoveToPositionCommand = ReactiveCommand.Create((double position) => MoveToPosition(position));
    }

    public DeviceSettingsViewModel DeviceSettings { get; }

    private void MoveToPosition(double newPosition)
    {
        try
        {
            _motor.MoveTo(newPosition);
        }
        catch (Exception e)
        {
            Dispatcher.UIThread.Invoke(() => 
                MessageBoxManager.GetMessageBoxStandard("Error Moving Motor to Position",
                e.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync());
        }
    }
}