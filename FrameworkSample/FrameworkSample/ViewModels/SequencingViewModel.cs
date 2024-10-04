using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Framework.Core.ViewModels;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Media;
using Devices.Utils;

using Framework.Devices;
using Framework.Services;
using FrameworkSample.Models;
using FrameworkSample.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace FrameworkSample.ViewModels;

public class SequencingViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    private readonly RunInfo _runInfo;
    private Dictionary<SequencingStep, string> _stepColors = new()
    {
        {SequencingStep.FocusCalibration, "yellow"},
        {SequencingStep.Imaging, "green"},
        {SequencingStep.Fluidics, "blue"}
    };
    private readonly SequencingService _sequencingService;
    private string StartingColor => "gray";

    [Reactive]
    public IImage Image { get; private set; }
    
    [Reactive]
    public string? StatusMessage { get; private set; }

    
    public SequencingViewModel(RunInfo runInfo, 
        SequencingService sequencingService,
        INavigationService<NavigationStates,NavigationTriggers> navigationService) 
        : base (navigationService, new [] {NavigationStates.ReadyToSequence, NavigationStates.Sequencing})
    {
        _sequencingService = sequencingService;
        TileColors = new ObservableCollection<ObservableCollection<string>>();
        
        _runInfo = runInfo;
        
        this.WhenAnyValue(x => x.ViewVisible)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(FillFLowcell);
        
        ReplaySubject<IImageBuffer> imageBufferSubject = new(3);
        _sequencingService.ImageCaptured += (img) => imageBufferSubject.OnNext(img);

        imageBufferSubject
            .Select(img => img.ConvertImage())
            .ObserveOn(RxApp.MainThreadScheduler)
            .BindTo(this, x => x.Image);
            
        _sequencingService.StepChanged += (step, cycle, lane, tile) =>
        {
            StatusMessage = $"Step: {step}, Cycle: {cycle+1}, Lane: {lane+1}, Tile: {tile+1}";
            if (!tile.HasValue)
            {
                // impacts whole lane
                for (var i = 0; i < _runInfo.NumTiles; i++)
                {
                    TileColors[lane][i] = _stepColors[step];
                }
            }
            else
            {
                TileColors[lane][tile.Value] = _stepColors[step];
                if (tile.Value == 0)
                {
                    for (var t = tile.Value + 1; t < _runInfo.NumTiles; t++)
                    {
                        TileColors[lane][t] = StartingColor;
                    }
                }
            }
        };
        _sequencingService.WhenAnyValue(x => x.State)
            .Subscribe(s =>
            {
                if (OperatingSystem.IsBrowser())
                    StatusMessage = "This demo does not support sequencing in the browser";
                else
                    StatusMessage = s switch
                    {
                        SequencingState.Idle => "Press 'Next' to start sequencing",
                        SequencingState.Running => "Sequencing started",
                        SequencingState.Cancelling => "Sequencing being cancelled",
                        SequencingState.Complete => "Sequencing complete",
                        _ => StatusMessage
                    };
            });
    }

   

    void FillFLowcell(bool isVisible)
    {
        if (!isVisible) return;
        TileColors.Clear();
        for (var i = 0; i < _runInfo.NumLanes; i++)
        {
            var lane = new ObservableCollection<string>();
            FillLane(lane, StartingColor);
            TileColors.Add(lane);
        }
    }

    void FillLane(ObservableCollection<string> lane, string color)
    {
        for (var j = 0; j < _runInfo.NumTiles; j++)
        {
            lane.Add(color);
        }
    }
    public ObservableCollection<ObservableCollection<string>> TileColors { get; set; }

    public override void Reset()
    {
        TileColors.Clear();
        Image = null;
        
        base.Reset();
    }
}