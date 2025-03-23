using Autofac;
using KJX.Config;
using KJX.Core;

namespace KJX.Tests;

public class ConfigurationHandlerTests
{
    private IContainer _container;
    private ConfigurationHandler _configHandler;
    [SetUp]
    public void SetUp()
    {
        var cfg = ConfigLoaderTests.LoadConfig("ConfigTestFiles/SystemConfigXYMotor.ini");
        var builder = new ContainerBuilder();
        _configHandler = new ConfigurationHandler();
        _configHandler.PopulateContainerBuilder(builder, cfg, true);
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
        Assert.That(motors.Count(), Is.EqualTo(4));
        Assert.That(motors.Any(x => x is DummyXMotor));
        Assert.That(motors.Any(x => x is DummyYMotor));
        Assert.That(motors.Any(x => x is DummyMotorWithNotifyPropertyChanged));
        Assert.That(motors.Any(x => x is DummyComboMotor));
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
    [Test]
    public void TestDefaultValuesAreSaved()
    {
        var x1 = _container.Resolve<DummyXMotor>();
        Assert.That(x1.IntProp, Is.EqualTo(1));
        var initialValues = _configHandler.GetInitialValues();
        Assert.That(initialValues.Count, Is.EqualTo(1));
        Assert.That(initialValues.First().Value.Count, Is.EqualTo(2));
        Assert.That(initialValues.First().Value["IntProp"], Is.EqualTo(1));
        Assert.That(initialValues.First().Value["DummyProp"], Is.EqualTo("I am X"));
        
        Assert.That(_configHandler.HasDirtyValues, Is.False);
        Assert.That(_configHandler.HasObjectsThatDoNotImplementINotifyPropertyChanged, Is.True);
        
        x1.IntProp = 2;
        // defaults are not overwritten
        Assert.That(initialValues.First().Value["IntProp"], Is.EqualTo(1));
        Assert.That(_configHandler.HasDirtyValues, Is.False);

        var x2 = _container.Resolve<DummyXMotor>();
        
        // make sure I didn't mess up the singleton behavior
        Assert.That(x2.IntProp, Is.EqualTo(2));
        
        // now resolve the one that supports INotifyPropertyChanged
        var x3 = _container.Resolve<DummyMotorWithNotifyPropertyChanged>();
        Assert.That(_configHandler.HasDirtyValues, Is.False);
        x3.IntProp = 3;
        Assert.That(_configHandler.HasDirtyValues, Is.True);
    }
    [Test]
    public void TestChangeBackToDefaultNotSaved()
    {
        var x1 = _container.Resolve<DummyMotorWithNotifyPropertyChanged>();
        x1.IntProp = 2;
        Assert.That(_configHandler.HasDirtyValues, Is.True);
        x1.IntProp = 0;
        Assert.That(_configHandler.HasDirtyValues, Is.False);
        Assert.That(_configHandler.GetChangedValues(), Is.Empty);
    }
    [Test]
    public void TestOnlyPropertiesWithGroupAttributeAreSaved()
    {
        var x1 = _container.Resolve<DummyMotorWithNotifyPropertyChanged>();
        x1.Position = 2;
        Assert.That(_configHandler.HasDirtyValues, Is.False);
        Assert.That(_configHandler.GetChangedValues(), Is.Empty);
    }
}
