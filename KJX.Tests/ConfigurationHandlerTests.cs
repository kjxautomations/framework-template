using Autofac;
using KJX.Config;
using KJX.Core;

namespace KJX.Tests;

public class ConfigurationHandlerTests
{
    private IContainer _container;
    [SetUp]
    public void SetUp()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigXYMotor.ini");
        var builder = new ContainerBuilder();
        ConfigurationHandler.PopulateContainerBuilder(builder, cfg);
        _container = builder.Build();
    }

    [TearDown]
    public void TearDown()
    {
        _container.Dispose();
    }
    [Test]
    public void TestObjectsAreSingletons()
    {
        var x1 = _container.ResolveKeyed<IMotorInterface>("XMotor");
        var x2 = _container.ResolveKeyed<IMotorInterface>("XMotor");
        Assert.That(x1, Is.Not.Null);
        Assert.That(x2, Is.SameAs(x1));
    }
    [Test]
    public void TestResolveAllByInterface()
    {
        var motors = _container.Resolve<IEnumerable<IMotorInterface>>();
        Assert.That(motors.Count(), Is.EqualTo(3));
        Assert.That(motors.Any(x => x is DummyXMotor));
        Assert.That(motors.Any(x => x is DummyYMotor));
    }

    [Test]
    public void TestPropertiesAreSet()
    {
        var x1 = _container.Resolve<DummyXMotor>();
        Assert.That(x1.IntProp, Is.EqualTo(1));
        Assert.That(x1.DummyProp, Is.EqualTo("I am X"));
    }
    [Test]
    public void TestKeyedInjection()
    {
        var x1 = _container.Resolve<DummyXMotor>();

        var combo = _container.Resolve<DummyComboMotor>();
        Assert.That(combo.XMotor, Is.SameAs(x1));
    }
}
