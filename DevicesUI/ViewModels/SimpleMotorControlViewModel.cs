using System.Reactive;
using Avalonia.Threading;
using Framework.Core.ViewModels;
using Framework.Devices;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace DevicesUI.ViewModels;

public class SimpleMotorControlViewModel : ViewModelBase
{
    private IMotor _motor;
    [Reactive] public string Name { get; set; }
    [Reactive] public double Position { get; set; }
    [Reactive] public double PositionToMoveTo { get; set; }
    [Reactive] public double Acceleration { get; set; }
    [Reactive] public double Velocity { get; set; }
    [Reactive] public string Units { get; set; }
    public ReactiveCommand<double, Unit> MoveToPositionCommand { get; set; }
    
    public SimpleMotorControlViewModel(IMotor motor)
    {
        _motor = motor;
        Name = motor.Name;
        Acceleration = _motor.Acceleration;
        Velocity = _motor.Velocity;
        Units = _motor.Units;
        
        this.WhenAnyValue(x => x.Acceleration).Subscribe(SetAcceleration);
        this.WhenAnyValue(x => x.Velocity).Subscribe(SetVelocity);
        _motor.WhenAnyValue(m => m.Position).BindTo(this, x => x.Position);
        
        MoveToPositionCommand = ReactiveCommand.Create((double position) => MoveToPosition(position));
    }

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

    private void SetAcceleration(double acceleration)
    {
        try
        {
            _motor.Acceleration = acceleration;
        }
        catch (ArgumentException e)
        {
            Dispatcher.UIThread.Invoke(() => 
                MessageBoxManager.GetMessageBoxStandard("Illegal Acceleration Parameter",
                e.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync());
        }
    }

    private void SetVelocity(double velocity)
    {
        try
        {
            _motor.Velocity = velocity;
        }
        catch (ArgumentException e)
        {
            Dispatcher.UIThread.Invoke(() => 
                MessageBoxManager.GetMessageBoxStandard("Illegal Velocity Parameter",
                e.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync());
        }
    }
}