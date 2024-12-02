using KJX.ProjectTemplate.Config;

namespace KJX.ProjectTemplate.Tests;

#pragma warning disable CS8602  
public class ConfigLoaderTests
{
    [Test]
    public void TestPropertyValidation()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationGoodConfig.ini");
        Assert.That(cfg.Count, Is.EqualTo(1)); 
        Assert.Throws<ConfigError>(() => ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationBadConfig.ini"));
    }

    [Test]
    public void TestLoadsNoSystemSection()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNoSystemType.ini");
        Assert.That(cfg.Count, Is.EqualTo(1)); 
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.That(xmotor, Is.Not.Null);
        Assert.That(typeof(DummyXMotor), Is.SameAs(xmotor.Type));
        Assert.That(xmotor.Interfaces.Contains(typeof(IInitializable)), Is.True); 
        Assert.That(xmotor.Interfaces.Contains(typeof(IMotorInterface)), Is.True); 

    }

    [Test]
    public void TestLoadsWithSystemSection()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigWithSystemType.ini", "ConfigTestFiles/SystemsDir");
        Assert.That(cfg.Count, Is.EqualTo(2)); 
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.That(xmotor, Is.Not.Null);
        Assert.That(typeof(AwesomeXMotor), Is.SameAs(xmotor.Type));
        Assert.That(xmotor.Interfaces.Contains(typeof(IInitializable)), Is.True); 
        Assert.That(xmotor.Interfaces.Contains(typeof(IMotorInterface)), Is.True); 
        Assert.That(xmotor.Properties["DummyProp"].ToString(), Is.EqualTo("Doo"));

        var ymotor = cfg.First(x => x.Name == "YMotor");
        Assert.That(ymotor, Is.Not.Null);
        Assert.That(typeof(DummyYMotor), Is.SameAs(ymotor.Type));
        Assert.That(ymotor.Interfaces.Contains(typeof(IInitializable)), Is.True); 
        Assert.That(ymotor.Interfaces.Contains(typeof(IMotorInterface)), Is.True); 
        Assert.That(ymotor.Properties["DummyProp"].ToString(), Is.EqualTo("Dabba"));

    }
    [Test]
    public void TestOverrideJustProperties()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigOverrideJustProperties.ini", "ConfigTestFiles/SystemsDir");
        Assert.That(cfg.Count, Is.EqualTo(2)); 
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.That(xmotor, Is.Not.Null);
        Assert.That(typeof(DummyXMotor), Is.SameAs(xmotor.Type));
        Assert.That(xmotor.Interfaces.Contains(typeof(IInitializable)), Is.True); 
        Assert.That(xmotor.Interfaces.Contains(typeof(IMotorInterface)), Is.True); 
        Assert.That(xmotor.Properties["DummyProp"].ToString(), Is.EqualTo("Wilma"));
    }
    
    [Test]
    public void TestEasySimulation()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigSimulatedMotor.ini", "ConfigTestFiles/SystemsDir");
        Assert.That(cfg.Count, Is.EqualTo(2)); 
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.That(xmotor, Is.Not.Null);
        Assert.That(typeof(SimulatedDummyXMotor), Is.SameAs(xmotor.Type));
        var ymotor = cfg.First(x => x.Name == "YMotor");
        Assert.That(ymotor, Is.Not.Null);
        Assert.That(typeof(DummyYMotor), Is.SameAs(ymotor.Type));
    }


    [Test]
    public void TestMissingType()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigMissingSectionType.ini", null));
        Assert.That(ex.InnerException.Message, Does.Contain("does not define a type")); 
    }


    [Test]
    public void TestLoadsBadSystemSection()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadSystemType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("does not exist")); 
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigMissingSystemType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("must contain SystemType")); 

        ex = Assert.Throws<ConfigError>(() => ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigWithSystemType.ini"));
        Assert.That(ex.InnerException.Message, Does.Contain("Null or empty")); 
    }

    [Test]
    public void TestLoadsBadTypes()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("NonexistantType")); 
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("NonexistantInterfaceType")); 

    }

    [Test]
    public void TestNoTypeDefinedThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNoType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("does not define a type")); 
    }

    [Test]
    public void TestBadPropertyThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadProperties.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("not found in type")); 
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadPropertiesType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.GetType(), Is.EqualTo(typeof(System.ArgumentException))); 
    }

    [Test]
    public void TestUnsupportedInterfaceThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigUnsupportedInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("does not support interface")); 
    }

    [Test]
    public void TestNotInterfaceThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNotInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.Message, Does.Contain("is not an interface")); 

    }
    
    [Test]
    public void TestClassWithRequiredProperties()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationBadRequiredPropertiesAttribute.ini"));

        Assert.That(ex.InnerException.Message, Does.Contain("RequiredString")); 
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationBadRequiredPropertiesKeyword.ini"));

        Assert.That(ex.InnerException.Message, Does.Contain("RequiredString"));
        
        // test the 2 happy paths
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationGoodRequiredPropertiesAttribute.ini");
        Assert.That(cfg.Count, Is.EqualTo(1));
        var obj = cfg.First();
        Assert.That(obj.Properties["RequiredString"].ToString(), Is.EqualTo("bar"));
        Assert.That(obj.Properties["NotRequiredInt"].ToString(), Is.EqualTo("3"));
        cfg = ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationGoodRequiredPropertiesKeyword.ini");
        Assert.That(cfg.Count, Is.EqualTo(1));
        obj = cfg.First();
        Assert.That(obj.Properties["RequiredString"].ToString(), Is.EqualTo("bar"));
        Assert.That(obj.Properties["NotRequiredInt"].ToString(), Is.EqualTo("3"));

    }
}
