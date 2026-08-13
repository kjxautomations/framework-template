using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace KJX.Devices.Logic;

public abstract class SensorBase : DeviceBase, ISensor, ISupportsSensorOverride
{
    /// <summary>
    /// How far behind a subscriber may fall before it starts losing its oldest readings. Bounded
    /// on purpose: a stalled consumer must not be able to grow a queue without limit or hold the
    /// sensor up.
    /// </summary>
    private const int SubscriptionQueueDepth = 256;

    private readonly object _subscriptionsLock = new();
    private readonly List<ReadingSubscription> _subscriptions = new();

    private double _value;
    private Func<double> _syntheticValueFunction;

    public string Name { get; init; }

    public double Value
    {
        get
        {
            if (_syntheticValueFunction != null)
                return _syntheticValueFunction();
            return _value;
        }
        protected set => SetField(ref _value, value);
    }

    public abstract void ReadSensor();

    public async IAsyncEnumerable<SensorReading> Readings(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscription = new ReadingSubscription(SubscriptionQueueDepth);
        lock (_subscriptionsLock)
            _subscriptions.Add(subscription);

        try
        {
            await foreach (var reading in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return reading;
        }
        finally
        {
            lock (_subscriptionsLock)
                _subscriptions.Remove(subscription);
        }
    }

    public void OverrideSensorValue(Func<double> valueFunction)
    {
        _syntheticValueFunction = valueFunction;
    }

    protected SensorBase(string name = "")
    {
        Name = name;
    }

    /// <summary>
    /// Publishes the current value to every subscriber. Call this on every read, including reads
    /// that produced the same value as the one before: subscribers are plotting a stream of
    /// samples, not watching for changes.
    /// </summary>
    protected void PublishReading()
    {
        var reading = new SensorReading(Value, DateTimeOffset.Now);

        lock (_subscriptionsLock)
        {
            foreach (var subscription in _subscriptions)
                subscription.Publish(reading);
        }
    }

    /// <summary>
    /// One subscriber's queue. Writing never blocks the sensor: once the queue is full the oldest
    /// reading is discarded, which is the right trade for a live plot.
    /// </summary>
    private sealed class ReadingSubscription
    {
        private readonly Channel<SensorReading> _channel;
        private long _droppedReadings;

        public ReadingSubscription(int queueDepth)
        {
            _channel = Channel.CreateBounded<SensorReading>(
                new BoundedChannelOptions(queueDepth)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                },
                _ => Interlocked.Increment(ref _droppedReadings));
        }

        public ChannelReader<SensorReading> Reader => _channel.Reader;

        /// <summary>
        /// Readings this subscriber missed because it fell behind. The RPC layer reports this to
        /// remote subscribers so that a gap in a plot is never silent.
        /// </summary>
        public long DroppedReadings => Interlocked.Read(ref _droppedReadings);

        public void Publish(SensorReading reading) => _channel.Writer.TryWrite(reading);
    }
}
