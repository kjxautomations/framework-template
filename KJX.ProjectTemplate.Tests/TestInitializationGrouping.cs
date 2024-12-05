using System.ComponentModel;
using System.Runtime.CompilerServices;
using KJX.ProjectTemplate.Devices;
using KJX.ProjectTemplate.Devices.Logic;

namespace KJX.ProjectTemplate.Tests;

public class XMotorInit : ISupportsInitialization, INotifyPropertyChanged
{
    private bool _isInitialized;

    public bool IsInitialized
    {
        get => _isInitialized;
        set => SetField(ref _isInitialized, value);
    }

    public void Initialize()
    {
        IsInitialized = true;
    }

    public void Shutdown()
    {
        IsInitialized = false;
    }

    public ushort InitializationGroup { get; set; } = 1;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class YMotorInit : XMotorInit
{
    public YMotorInit()
    {
        InitializationGroup = 0; // force it to go first
    }
}

[TestFixture]
public class TestInitializationGrouping
{
    [Test]
    public void TestGroupsInitializationAndShutdown()
    {
        var notifications = new List<object>();
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            notifications.Add(sender);
        }
        var devices = new[] { new XMotorInit(), new YMotorInit() };
        devices[0].PropertyChanged += OnPropertyChanged;
        devices[1].PropertyChanged += OnPropertyChanged;

        Initializer.Initialize(devices);
        Assert.That(notifications[0], Is.SameAs(devices[1]));
        Assert.That(notifications[1], Is.SameAs(devices[0]));
        
        notifications.Clear();
        Initializer.Shutdown(devices);
        Assert.That(notifications[0], Is.SameAs(devices[0]));
        Assert.That(notifications[1], Is.SameAs(devices[1]));

    }
}