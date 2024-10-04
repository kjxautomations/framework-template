using Framework.Config;

namespace FrameworkTest;

#pragma warning disable CS8602  
public class ConfigLoaderTests
{
    [Test]
    public void TestPropertyValidation()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationGoodConfig.ini");
        Assert.True(cfg.Count == 1);
        Assert.Throws<Framework.Config.ConfigError>(() => ConfigLoader.LoadConfig("ConfigTestFiles/PropertyValidationBadConfig.ini"));
    }
    
    [Test]
    public void TestLoadsNoSystemSection()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNoSystemType.ini");
        Assert.True(cfg.Count == 1);
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.NotNull(xmotor);
        Assert.That(typeof(DummyXMotor), Is.SameAs(xmotor.Type));
        Assert.True(xmotor.Interfaces.Contains(typeof(IInitializable)));
        Assert.True(xmotor.Interfaces.Contains(typeof(IMotorInterface)));
        
    }
    
    [Test]
    public void TestLoadsWithSystemSection()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigWithSystemType.ini", "ConfigTestFiles/SystemsDir");
        Assert.True(cfg.Count == 2);
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.NotNull(xmotor);
        Assert.That(typeof(AwesomeXMotor), Is.SameAs(xmotor.Type));
        Assert.True(xmotor.Interfaces.Contains(typeof(IInitializable)));
        Assert.True(xmotor.Interfaces.Contains(typeof(IMotorInterface)));
        Assert.That(xmotor.Properties["DummyProp"].ToString(), Is.EqualTo("Doo"));
        
        var ymotor = cfg.First(x => x.Name == "YMotor");
        Assert.NotNull(ymotor);
        Assert.That(typeof(DummyYMotor), Is.SameAs(ymotor.Type));
        Assert.True(ymotor.Interfaces.Contains(typeof(IInitializable)));
        Assert.True(ymotor.Interfaces.Contains(typeof(IMotorInterface)));
        Assert.That(ymotor.Properties["DummyProp"].ToString(), Is.EqualTo("Dabba"));

    }
    [Test]
    public void TestOverrideJustProperties()
    {
        var cfg = ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigOverrideJustProperties.ini", "ConfigTestFiles/SystemsDir");
        Assert.True(cfg.Count == 2);
        var xmotor = cfg.First(x => x.Name == "XMotor");
        Assert.NotNull(xmotor);
        Assert.That(typeof(DummyXMotor), Is.SameAs(xmotor.Type));
        Assert.True(xmotor.Interfaces.Contains(typeof(IInitializable)));
        Assert.True(xmotor.Interfaces.Contains(typeof(IMotorInterface)));
        Assert.That(xmotor.Properties["DummyProp"].ToString(), Is.EqualTo("Wilma"));
    }

    [Test]
    public void TestMissingType()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigMissingSectionType.ini", null));
        Assert.True(ex.InnerException.Message.Contains("does not define a type"));
    }


    [Test]
    public void TestLoadsBadSystemSection()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadSystemType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("does not exist"));
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigMissingSystemType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("must contain SystemType"));
        
        ex = Assert.Throws<ConfigError>(() => ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigWithSystemType.ini"));
        Assert.True(ex.InnerException.Message.Contains("Null or empty"));
    }

    [Test]
    public void TestLoadsBadTypes()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("NonexistantType"));
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("NonexistantInterfaceType"));

    }

    [Test]
    public void TestNoTypeDefinedThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNoType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("does not define a type"));
    }

    [Test]
    public void TestBadPropertyThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadProperties.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("not found in type"));
        ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigBadPropertiesType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.That(ex.InnerException.GetType(), Is.EqualTo(typeof(System.ArgumentException)));
    }

    [Test]
    public void TestUnsupportedInterfaceThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigUnsupportedInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("does not support interface"));
    }

    [Test]
    public void TestNotInterfaceThrows()
    {
        var ex = Assert.Throws<ConfigError>(() =>
            ConfigLoader.LoadConfig("ConfigTestFiles/SystemConfigNotInterfaceType.ini", "ConfigTestFiles/SystemsDir"));
        Assert.True(ex.InnerException.Message.Contains("is not an interface"));

    }
}