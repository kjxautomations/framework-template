using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac.Features.AttributeFilters;
using Framework.Devices;
using Framework.Devices.Logic;
using Framework.Services;
using Framework.Services;
using FrameworkSample.Models;
using ReactiveUI;

namespace FrameworkSample.Services;

public class TemperatureMonitoringServiceConfig
{
    public required int IntervalMs { get; init; }
    public required double Threshold { get; init; }

}

public class TemperatureMonitoringService : IBackgroundService
{
    private readonly INotificationService _notificationService;
    private readonly Queue<(DateTime Timestamp, double Value)> _values = new();
    private double _sum = 0;
    private readonly ISensor _sensorToMonitor;
    private bool _isOverThreshold;
    private readonly INavigationService<NavigationStates, NavigationTriggers> _navigationService;
    private readonly TemperatureMonitoringServiceConfig _config;
    private readonly SequencingService _sequencingService;


    public TemperatureMonitoringService(SequencingService sequencingService,
        INotificationService notificationService,
        INavigationService<NavigationStates, NavigationTriggers> navigationService,
        [KeyFilter("TemperatureSensor1")]ISensor sensorToMonitor,
        TemperatureMonitoringServiceConfig config)
    {
        _notificationService = notificationService;
        _sensorToMonitor = sensorToMonitor;
        _navigationService = navigationService;
        _sequencingService = sequencingService;
        _config = config;
    }
    
    private void ValueUpdated(double value)
    {
        _sum += value;
        // add the new value to the list
        _values.Enqueue((DateTime.Now, value));
        bool haveEnoughValues = false;
        var cutoff = DateTime.Now.Subtract(TimeSpan.FromMilliseconds(_config.IntervalMs));
        while (_values.Count > 0 && _values.Peek().Timestamp < cutoff)
        {
            _sum -= _values.Dequeue().Value;
            haveEnoughValues = true;
        }

        // issue an error notification and stop sequencing if the average exceeds the threshold
        // for the configured period of time
        var average = _values.Count > 0 ? _sum / _values.Count : 0;
        if (average >= _config.Threshold && haveEnoughValues)
        {
            StopMonitoring();
            _notificationService.AddNotification(NotificationType.Error, $"Temperature threshold of {_config.Threshold} exceeded for {_config.IntervalMs/1000.0} seconds");
            _navigationService.SendTrigger(NavigationTriggers.Abort);
        }
        else if (value >= _config.Threshold && !_isOverThreshold)
        {
            _notificationService.AddNotification(NotificationType.Warning, $"Temperature threshold of {_config.Threshold} exceeded");
            _isOverThreshold = true;
        }
        else if (value < _config.Threshold)
        {
            _isOverThreshold = false;
        }
    }
    private void OnSequencingStateChanged(SequencingState state)
    {
        switch (state)
        {
            case SequencingState.Running:
                StartMonitoring();
                break;
            case SequencingState.Complete:
            case SequencingState.Cancelling:
                StopMonitoring();
                break;
        }
    }

    private void StopMonitoring()
    {
        _sensorToMonitor.ValueUpdated -= ValueUpdated;
    }

    private void StartMonitoring()
    {
        _sum = 0;
        _values.Clear();
        _sensorToMonitor.ValueUpdated += ValueUpdated;
    }

    public void Start()
    {
        _sequencingService.WhenAnyValue(x => x.State)
            .Subscribe(OnSequencingStateChanged);
    }
}