using KJX.Config;

namespace KJX.Tests;

// little hack class that intercepts the Dispose of MemoryStream and saves off the contents
public class MemoryStreamSpy : MemoryStream
{
    public MemoryStreamSpy()
    {
    }

    public byte[]? SavedBuffer { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SavedBuffer = ToArray();
        }

        base.Dispose(disposing);
    }
}

[TestFixture]
public class ConfigWriterTests
{
    [Test]
    public void TestSetValuesWhenItemOnlyInSystemCreatesSection()
    {
        var result = new MemoryStreamSpy();
        using (var cfgFile = File.OpenRead("ConfigTestFiles/SystemConfigWithSystemType.ini"))
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
            ConfigWriter.SaveEditedConfig(cfgFile, result, changes);
        }
        var fileContents = System.Text.Encoding.UTF8.GetString(result.SavedBuffer!);
        Assert.That(fileContents, Is.EqualTo(
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
        var result = new MemoryStreamSpy();
        using (var cfgFile = File.OpenRead("ConfigTestFiles/SystemConfigWithSystemType.ini"))
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
            ConfigWriter.SaveEditedConfig(cfgFile, result, changes);
        }
        var fileContents = System.Text.Encoding.UTF8.GetString(result.SavedBuffer!);
        Assert.That(fileContents, Is.EqualTo(
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