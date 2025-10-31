using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Timers;
using Avalonia.Media;
using ReactiveUI.Avalonia;
using KJX.Core.ViewModels;
using Devices.Utils;
using DynamicData;
using KJX.Devices;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Size = System.Drawing.Size;
using Timer = System.Timers.Timer;

namespace KJX.DevicesUI.ViewModels;

public class SimpleCameraViewModel : ViewModelBase
{
    public string Name { get; }
    public DeviceSettingsViewModel DeviceSettings { get; }

    private readonly ICamera _camera;

    public SimpleCameraViewModel(ICamera camera)
    {
        _camera = camera;
        Name = camera.Name;
        Initialize = ReactiveCommand.CreateFromTask(DoInitialize,
            _camera.WhenAnyValue(x => x.IsInitialized)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Select(x => !x));
        DeviceSettings = new DeviceSettingsViewModel(_camera);
        _camera.WhenAnyValue(x => x.IsInitialized)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(x =>
            {
                IsInitialized = x;
                SupportedResolutions.Clear();

                if (IsInitialized)
                {
                    SupportedResolutions.AddRange(_camera.SupportedResolutions());
                    SelectedResolutionIndex = SupportedResolutions.IndexOf(_camera.Resolution);
                    SupportsResolution = SupportedResolutions.Count > 0;
                }
                else
                {
                    SupportsResolution = false;
                }

            });
        this.WhenAnyValue(x => x.StreamImages).Subscribe(DoStartStopCapture);
        this.WhenAnyValue(x => x.SelectedResolutionIndex)
            .Select(async x => await DoSetResolution(x)).Subscribe(result => { });

    }
    
    [Reactive]
    public bool StreamImages { get; set; }
    
    [Reactive]
    public IImage Image { get; private set; }

    private Timer _timer = new Timer(TimeSpan.FromMilliseconds(50)) { AutoReset = false };
    void TimerOnElapsed(object sender, ElapsedEventArgs e)
    {
        var img = _camera.GetImage();
        Image = img.ConvertImage();
        _timer.Start();
    }

    void DoStartStopCapture(bool enabled)
    {
        if (enabled)
        {
            _timer.Elapsed += TimerOnElapsed;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _timer.Elapsed -= TimerOnElapsed;
        }
        
    }

    
    private async Task DoInitialize()
    {
        await Task.Run(_camera.Initialize);
    }
    private async Task DoSetResolution(int newResolutionIndex)
    {
        if (newResolutionIndex >= 0 && newResolutionIndex < SupportedResolutions.Count)
        {
            var selectedResolution = SupportedResolutions[newResolutionIndex];
            await Task.Run(() => _camera.Resolution = selectedResolution);
        }
    }


    [Reactive]
    public bool SupportsResolution { get; private set; }
    
    public ReactiveCommand<Unit, Unit> Initialize { get; }
    
    [Reactive]
    public bool IsInitialized { get; private set; }
    public ObservableCollection<Size> SupportedResolutions { get; } = new ObservableCollection<Size>();
    
    
    [Reactive] 
    public int SelectedResolutionIndex { get; set; }

}