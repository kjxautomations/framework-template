using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Media;
using Devices.Utils;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using KJX.Devices;
using KJX.ProjectTemplate.Control.Models;
using KJX.ProjectTemplate.Control.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;


namespace KJX.ProjectTemplate.Control.ViewModels;

public class SequencingScreenViewModel : StateViewModelBase<NavigationStates, NavigationTriggers>
{
    private readonly RunInfo _runInfo;
    private readonly SequencingService _sequencingService;
    
    private Dictionary<SequencingStep, string> _stepColors = new()
    {
        {SequencingStep.FocusCalibration, "yellow"},
        {SequencingStep.Imaging, "green"},
        {SequencingStep.Fluidics, "blue"}
    };

    private static string StartingColor => "gray";
    [Reactive] public IImage Image { get; private set; }
    [Reactive] public string? StatusMessage { get; private set; }
    public ObservableCollection<ObservableCollection<string>> TileColors { get; set; } = [];

    
    public SequencingScreenViewModel(RunInfo runInfo, SequencingService sequencingService, 
        INavigationService<NavigationStates,NavigationTriggers> navigationService) 
        : base (navigationService, new [] {NavigationStates.ReadyToSequence, NavigationStates.Sequencing})
    {
        _sequencingService = sequencingService;
        _runInfo = runInfo;
        
        ReplaySubject<IImageBuffer> imageBufferSubject = new(3);
        
        this.WhenAnyValue(x => x.ViewVisible)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(FillFLowcell);
        
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
        
        _sequencingService.ImageCaptured += (img) => imageBufferSubject.OnNext(img);

        imageBufferSubject
            .Select(img => img.ConvertImage())
            .ObserveOn(RxApp.MainThreadScheduler)
            .BindTo(this, x => x.Image);
    }

    private void FillFLowcell(bool isVisible)
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

    private void FillLane(ObservableCollection<string> lane, string color)
    {
        for (var j = 0; j < _runInfo.NumTiles; j++)
        {
            lane.Add(color);
        }
    }
}