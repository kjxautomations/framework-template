using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Autofac.Features.AttributeFilters;
using KJX.Config;
using KJX.Devices.Logic;

namespace KJX.Tests;


public interface IMotorInterface
{
    void MoveTo(double location);
}

public interface IInitializable
{
    void DoInitialize();
    bool IsInitialized { get; }
}

public class DummyMotor : IMotorInterface, IInitializable
{
    [Group("Basic")]
    public string? DummyProp { get; init; }
    [Group("Basic")]
    public int IntProp { get; set; }
    public void MoveTo(double location)
    {
        throw new NotImplementedException();
    }

    public void DoInitialize()
    {
        throw new NotImplementedException();
    }

    public bool IsInitialized => true;
}

public class DummyXMotor : DummyMotor
{
}

public class DummyMotorWithNotifyPropertyChanged : IMotorInterface, IInitializable, INotifyPropertyChanged
{
    private double _position;
    private int _intProp;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [Group("Basic")]
    public int IntProp
    {
        get => _intProp;
        set
        {
            if (value == _intProp) return;
            _intProp = value;
            RaisePropertyChanged(nameof(IntProp));
        }
    }

    public void MoveTo(double location)
    {
        Position = location;
    }

    public double Position
    {
        get => _position;
        set
        {
            if (value.Equals(_position)) return;
            _position = value;
            RaisePropertyChanged(nameof(Position));
        }
    }

    public void DoInitialize()
    {
        IsInitialized = true;
        
    }

    public bool IsInitialized { get; private set; }
}

public class DummyDataObjectWithoutINotifyPropertyChanged
{
    [Group("Basic")]
    public int IntProp { get; set; }
    [Group("Basic")]
    public string StringProp { get; set; } = "A good value";
}

public class SimulatedDummyXMotor : DummyMotor
{
}

public class DummyYMotor : DummyMotor
{
}

public class AwesomeXMotor : DummyMotor
{
}

public class DummyComboMotor : IMotorInterface, IInitializable
{
    public DummyComboMotor([KeyFilter("XMotor")] IMotorInterface x)
    {
        XMotor = x;
    }
    public IMotorInterface XMotor { get;  }

    public void MoveTo(double location)
    {
        throw new NotImplementedException();
    }

    public void DoInitialize()
    {
        throw new NotImplementedException();
    }

    public bool IsInitialized { get; }
}

public class ObjectWithPropertyValidation
{
    [Required] public string? RequiredString { get; set; }

    [System.ComponentModel.DataAnnotations.Range(0, 100)]
    public int RangeInt { get; set; }
}

public interface IUnsupportedInterface
{
}

public class ObjectWithRequiredPropertiesAttribute
{
    [Required] public string? RequiredString { get; set; }

    public int NotRequiredInt { get; set; }
}
public class ObjectWithRequiredPropertiesKeyword
{
    public required string? RequiredString { get; set; }

    public int NotRequiredInt { get; set; }
    
    // we don't verify these - leave these for the DI container
    public required IInitializable? RequiredObject { get; set; }
}



public class BackgroundThreadSynchronizationContext : SynchronizationContext
{
    private readonly Thread _thread;
    private volatile bool _cancel;

    class Callback
    {
        public SendOrPostCallback cb;
        public object state;

        public Callback(SendOrPostCallback callback, object s)
        {
            cb = callback;
            state = s;
        }

        public void Invoke()
        {
            cb(state);
        }
    }

    public BackgroundThreadSynchronizationContext()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            while (!_cancel)
            {
                var message = GetMessage();
                if (message != null)
                {
                    message.Invoke();
                }
                else
                {
                    Thread.Sleep(100); // Or use a more efficient waiting mechanism
                }
            }
        });
        _thread.Start();
    }

    public void Shutdown()
    {
        _cancel = true;
        _thread.Join();
    }

    private Queue<Callback> _queue = new Queue<Callback>();

    private Callback GetMessage()
    {
        lock (_queue)
        {
            return _queue.Count > 0 ? _queue.Dequeue() : null;
        }
    }

    public override void Post(SendOrPostCallback d, object state)
    {
        lock (_queue)
        {
            _queue.Enqueue(new Callback(d, state));
        }
    }

    public override void Send(SendOrPostCallback d, object state)
    {
        d(state);
    }

    public override SynchronizationContext CreateCopy()
    {
        return this; // Or create a new instance if needed
    }
}

public enum TestEnum
{
    Value1,
    Value2,
    Value3
};

public class DummyDeviceWithProperties : DeviceBase
{
    protected override void DoInitialize()
    {
        
    }
    
    public void SetBusy(bool busy)
    {
        IsBusy = busy;
    }

    [Group("Basic")] 
    public string Basic1 { get; set; } = "foo";

    [Group("Basic")]
    [RangeIncrement(-10, 10, 0.1)]
    public float Basic2 { get; set; } = 5;

    [Group("Basic")]
    public bool Basic3 { get; set; } = true;

    [Group("Basic")]
    public TestEnum Basic4 { get; set; } = TestEnum.Value2;

    [RangeIncrement(-10, 10, 0.1)]
    [Group("Advanced1")]
    public double Advanced1 { get; set; } = 6;

    [RangeIncrement(0, 100, 1)]
    [Group("Advanced2")]
    public int Advanced2 { get; set; } = 7;
    [Group("Advanced2")]
    public int Advanced3 { get; set; } = 7;
}