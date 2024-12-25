using KJX.ProjectTemplate.Devices;
using KJX.ProjectTemplate.Devices.FirmwareProtocol;
using Microsoft.Extensions.Logging;
using Moq;

namespace KJX.ProjectTemplate.Tests;

[Ignore("This test requires a device to be connected")]
[TestFixture]
public class FirmwareConnectionTestHarness
{
    [Test]
    public void TestConnection()
    {
        var logger = new Mock<ILogger<FirmwareConnection>>().Object;
        var connection = new FirmwareConnection("192.168.68.5", 9968) { Logger = logger };

        connection.Initialize();

        var versions = connection.GetFirmwareVersions();
        Assert.That(versions, Is.Not.Null);
        Assert.That(versions.MainVersion, Is.EqualTo(123));
        for (uint i=0;i<100;i++)
        {
            var response = connection.GetResponse(NodeId.Main, new Request { Ping = new Ping() { Id = i } });
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Pong.Id, Is.EqualTo(i));
        }
        FirmwareLed led1 = new(connection, LedType.Led1) { Logger = new Mock<ILogger<FirmwareLed>>().Object };
        FirmwareLed led2 = new(connection, LedType.Led2) { Logger = new Mock<ILogger<FirmwareLed>>().Object };
        FirmwareLed led3 = new(connection, LedType.Led3) { Logger = new Mock<ILogger<FirmwareLed>>().Object };
        led1.Initialize();
        led2.Initialize();
        led3.Initialize();
        led1.Enabled = true;
        led2.Enabled = true;
        led3.Enabled = true;
        Thread.Sleep(1000);
        led1.Enabled = false;
        led2.Enabled = false;
        led3.Enabled = false;
        connection.Shutdown();
    }
}