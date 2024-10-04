using System.ComponentModel;
using Framework.Devices;
using Microsoft.Extensions.Logging;
using Moq;

namespace FrameworkTest;

[TestFixture]
public class TestSimulatedLinearMotor
{
    [Test]
    public void TestPropertiesNotify()
    {
        var logger = new Mock<ILogger<SimulatedLinearStepperMotor>>().Object;
        var motor = new SimulatedLinearStepperMotor { Acceleration = 1.0, Velocity = 1.0, Logger = logger};
        motor.PropertyChanged += MotorOnPropertyChanged;
        string propertyName = null;

        void MotorOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            propertyName = e.PropertyName;
        }
        motor.Initialize();
        Assert.That(propertyName, Is.EqualTo("IsInitialized"));

        motor.Home();
        Assert.That(propertyName, Is.EqualTo("IsHomed"));
        motor.MoveTo(1);
        Assert.That(propertyName, Is.EqualTo("Position"));

    }
}