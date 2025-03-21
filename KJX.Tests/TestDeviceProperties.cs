namespace KJX.Tests;

[TestFixture]
public class TestDeviceProperties
{
    [Test]
    public void TestDevicePropertiesWork()
    {
        var dummyDevice = new DummyDeviceWithProperties();
        
        var vm = new DeviceSettingsViewModel(dummyDevice);
        
        Assert.That(vm.BasicProperties, Is.Not.Null);
        Assert.That(vm.BasicProperties.Count, Is.EqualTo(4));

        var basic1 = vm.BasicProperties.First((x) => x.Name.Equals("Basic1"));
        Assert.That(basic1, Is.Not.Null);
        Assert.That(basic1.IsString, Is.True);
        Assert.That(basic1.IsEnum, Is.False);
        Assert.That(basic1.IsNumeric, Is.False);
        Assert.That(basic1.IsBoolean, Is.False);
        basic1.Value = "bar";
        Assert.That(dummyDevice.Basic1, Is.EqualTo("bar"));
        
        var basic2 = vm.BasicProperties.First((x) => x.Name.Equals("Basic2"));
        Assert.That(basic2, Is.Not.Null);
        Assert.That(basic2.IsString, Is.False); 
        Assert.That(basic2.IsEnum, Is.False);
        Assert.That(basic2.IsNumeric, Is.True);
        Assert.That(basic2.IsBoolean, Is.False);
        basic2.Value = 3;
        Assert.That(dummyDevice.Basic2, Is.EqualTo(3));
        
        var basic3 = vm.BasicProperties.First((x) => x.Name.Equals("Basic3"));
        Assert.That(basic3, Is.Not.Null);
        Assert.That(basic3.IsString, Is.False);
        Assert.That(basic3.IsEnum, Is.False);
        Assert.That(basic3.IsNumeric, Is.False);
        Assert.That(basic3.IsBoolean, Is.True);
        basic3.Value = true;
        Assert.That(basic3.Value, Is.True);
        basic3.Value = false;
        Assert.That(basic3.Value, Is.False);
        
        var basic4 = vm.BasicProperties.First(x => x.Name.Equals("Basic4"));
        Assert.That(basic4, Is.Not.Null);
        Assert.That(basic4.IsString, Is.False);
        Assert.That(basic4.IsEnum, Is.True);
        Assert.That(basic4.IsNumeric, Is.False);
        Assert.That(basic4.IsBoolean, Is.False);
        Assert.That(basic4.EnumValues, Is.EquivalentTo(new [] {TestEnum.Value1, TestEnum.Value2, TestEnum.Value3}));

        basic4.Value = TestEnum.Value3;
        Assert.That(dummyDevice.Basic4, Is.EqualTo(TestEnum.Value3));

        var adv1 = vm.AdvancedProperties.First(x => x.Key == "Advanced1").Value;
        Assert.That(adv1.Count, Is.EqualTo(1));
        
        var adv2 = vm.AdvancedProperties.First(x => x.Key == "Advanced2").Value;
        Assert.That(adv2.Count, Is.EqualTo(2));

        Assert.Throws<ArgumentOutOfRangeException>(() => basic2.Value = 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => basic2.Value = -100);
        Assert.DoesNotThrow(() => basic2.Value = 3);
        
        dummyDevice.SetBusy(true);
        Assert.Throws<InvalidOperationException>(() => basic2.Value = 1);
        dummyDevice.SetBusy(false);
        Assert.That(dummyDevice.Basic2, Is.EqualTo(3));
        

    }
    
}