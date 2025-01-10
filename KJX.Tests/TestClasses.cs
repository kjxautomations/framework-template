using System.ComponentModel.DataAnnotations;
using Autofac.Features.AttributeFilters;

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
    public string? DummyProp { get; init; }
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