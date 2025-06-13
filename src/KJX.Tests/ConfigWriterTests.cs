using KJX.Config;

namespace KJX.Tests;

[TestFixture]
public class ConfigWriterTests
{
    [Test]
    public void TestSetValuesWhenItemOnlyInSystemCreatesSection()
    {
        string result;
        using (var cfgFile = new StreamReader("ConfigTestFiles/SystemConfigWithSystemType.ini"))
        {
            var changes = new Dictionary<string, Dictionary<string, object>>
            {
                {
                    "YMotor", new Dictionary<string, object>
                    {
                        { "DummyProp", "DooDoo" }
                    }
                }
            };
            result = ConfigWriter.SaveEditedConfig(cfgFile, changes);
        }
        
        Assert.That(result, Is.EqualTo(
@"[System]
SystemType= TestSystem

; here's a comment for a section
[XMotor]
; here's a comment for a property
_type= KJX.Tests.AwesomeXMotor, KJX.Tests
_interface1= KJX.Tests.IInitializable, KJX.Tests
_interface2= KJX.Tests.IMotorInterface, KJX.Tests
DummyProp= Doo
[YMotor]
DummyProp=DooDoo
"
));
   }
    [Test]
    public void TestSetValuesWhenItemIsInMainConfig()
    {
        string result;
        using (var cfgFile = new StreamReader("ConfigTestFiles/SystemConfigWithSystemType.ini"))
        {
            var changes = new Dictionary<string, Dictionary<string, object>>
            {
                {
                    "XMotor", new Dictionary<string, object>
                    {
                        { "DummyProp", "DooDoo" }
                    }
                }
            };
            result = ConfigWriter.SaveEditedConfig(cfgFile, changes);
        }
        
        Assert.That(result, Is.EqualTo(
            @"[System]
SystemType= TestSystem

; here's a comment for a section
[XMotor]
; here's a comment for a property
_type= KJX.Tests.AwesomeXMotor, KJX.Tests
_interface1= KJX.Tests.IInitializable, KJX.Tests
_interface2= KJX.Tests.IMotorInterface, KJX.Tests
DummyProp= DooDoo
"
        ));
    }
}