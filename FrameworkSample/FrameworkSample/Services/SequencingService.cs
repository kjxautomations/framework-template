using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac.Features.AttributeFilters;
using Framework.Devices;
using FrameworkSample.Models;

namespace FrameworkSample.Services;

public enum SequencingStep
{
    FocusCalibration,
    Imaging,
    Fluidics
}

public delegate void StepChanged(SequencingStep step, int cycle, int lane, int? tile);

public enum SequencingState
{
    Idle,
    Running,
    Complete,
    Cancelling,
    Cancelled
}

public class SequencingService : INotifyPropertyChanged
{
    public SequencingState State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    public event Action<IImageBuffer>? ImageCaptured;
    public event StepChanged? StepChanged;
    

    public SequencingService(RunInfo runInfo, ICamera camera, 
        [KeyFilter("XMotor")] IMotor xMotor, 
        [KeyFilter("YMotor")] IMotor yMotor,
        [KeyFilter("ZMotor")] IMotor zMotor)
    { 
        _runInfo = runInfo;
        _camera = camera;
        _xMotor = xMotor;
        _yMotor = yMotor;
        _zMotor = zMotor;
    }
    public void StartSequencing()
    {
        if (State != SequencingState.Idle)
            throw new InvalidOperationException("Sequencing already in progress");
        State = SequencingState.Running;
        _cancel = false;
        _sequencingTask = Task.Run(() =>
        {
            try
            {
                // just assuming lanes and tiles are all 1mm per side. A real service would
                // have to do some math to determine the actual distance to move

                // first do a mock focus calibration
                for (var lane = 0; lane < _runInfo.NumLanes; lane++)
                {
                    for (var tile = 0; tile < _runInfo.NumTiles; tile++)
                    {
                        StepChanged?.Invoke(SequencingStep.FocusCalibration, 0, lane, tile);
                        _yMotor.MoveTo(lane);
                        _xMotor.MoveTo(tile);
                        // mock focus calibration assumes focus is at 0
                        for (var i = -5; i <= 5; i++)
                        {
                            if (_cancel)
                                return;
                            var z = i * 0.1;
                            _zMotor.MoveTo(z);
                            var img = _camera.GetImage();
                            
                            ImageCaptured?.Invoke(img);
                        }
                    }
                }
                // next, do mock imaging and fluidics
                for (var cycle = 0; cycle < _runInfo.NumCycles; cycle++)
                {
                    for (var lane = 0; lane < _runInfo.NumLanes; lane++)
                    {
                        for (var tile = 0; tile < _runInfo.NumTiles; tile++)
                        {
                            if (_cancel)
                                return;
                            StepChanged?.Invoke(SequencingStep.Imaging, cycle, lane, tile);
                            _yMotor.MoveTo(lane);
                            _xMotor.MoveTo(tile);
                            _zMotor.MoveTo(0);
                            var img = _camera.GetImage();
                            ImageCaptured?.Invoke(img);
                            Task.Delay(100);
                        }
                    }
                    StepChanged?.Invoke(SequencingStep.Fluidics, cycle, 0, null);
                }
            }
            finally
            {
                if (_cancel)
                    State = SequencingState.Cancelled;
                else
                    State = SequencingState.Complete;
            }
            
        });
    }
    public void CancelSequencing()
    {
        if (State != SequencingState.Running)
            throw new InvalidOperationException("No sequencing in progress to cancel");
        State = SequencingState.Cancelling;
        _cancel = true;
        _sequencingTask?.Wait();
        _sequencingTask?.Dispose();
        _sequencingTask = null;
        State = SequencingState.Cancelled;
    }

    private readonly RunInfo _runInfo;
    private readonly ICamera _camera;
    private readonly IMotor _xMotor;
    private readonly IMotor _yMotor;
    private Task? _sequencingTask;
    private readonly IMotor _zMotor;
    private bool _cancel;
    private SequencingState _state;


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

    public void Reset()
    {
        if (State == SequencingState.Running || State == SequencingState.Cancelling)
            throw new InvalidOperationException("Cannot reset while sequencing is in progress");
        State = SequencingState.Idle;
    }
}