using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using KJX.ProjectTemplate.Core.ViewModels;
using Core.Stylesheets;
using DynamicData;
using KJX.ProjectTemplate.Devices;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.DevicesUI.ViewModels;

public class SimpleSensorViewModel : ViewModelBase
{
    private readonly ISensor _sensor;
    public string Name { get; }
    [Reactive] public double Value { get; set; }
    [Reactive] public bool RecordingEnabled { get; set; } = true;
    [Reactive] public string EnableRecordingButtonContent { get; set; } = "Stop Recording";
    
    public ReactiveCommand<Unit, Unit> InitializeCommand { get; }
    
    public ISeries[] Series { get; set; }
    public ObservableCollection<ObservableValue> ChartSeries { get; } = [];

    //set axes and frame styles
    public Axis[] XAxis { get; } = CartesianChartStyles.AxesStyles.XAxis;
    public Axis[] YAxis { get; } = CartesianChartStyles.AxesStyles.YAxis;
    public DrawMarginFrame MarginFrame { get; } = CartesianChartStyles.MarginFrame;

    public SimpleSensorViewModel(ISensor sensor)
    {
        Name = sensor.Name;
        _sensor = sensor;
            

        ChartSeries.AddRange(Enumerable.Range(0, 150).Select(x => new ObservableValue(null)));
        Series = new ISeries[]
        {
            new LineSeries<ObservableValue>()
            {
                Values = ChartSeries,
                GeometrySize = CartesianChartStyles.GeometrySize,
                Stroke = CartesianChartStyles.StrokeColor,
                Fill = null,
                GeometryStroke = CartesianChartStyles.StrokeColor
            }
        };

        XAxis.FirstOrDefault()!.Name = "Count";
        YAxis.FirstOrDefault()!.Name = "Reading";
        
        sensor.ValueUpdated += (value) =>
        {
            if (RecordingEnabled) 
                SetValue(value);
        };
        
        InitializeCommand = 
            ReactiveCommand.CreateFromTask(InitializeSensor, 
                _sensor.WhenAnyValue(x => x.IsInitialized)
                    .Select(x => !x)
                    .ObserveOn(AvaloniaScheduler.Instance));
            
    }
    private async Task InitializeSensor()
    {
        await Task.Run(() => _sensor.Initialize());
    }

    private void SetValue(double value)
    {
        // run on UI thread
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                ChartSeries.RemoveAt(0);
                ChartSeries.Add(new ObservableValue(value));
                Value = value;
            });
        }
        catch (Exception e)
        {
            // this catches a timing issue when the UI is closed. It is safe to eat this exception
        }

    }

    public void EnableDisableRecording()
    {
        RecordingEnabled = !RecordingEnabled;
        EnableRecordingButtonContent = RecordingEnabled ? "Stop Recording" : "Start Recording";
    }
}