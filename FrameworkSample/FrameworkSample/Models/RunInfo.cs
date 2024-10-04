using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;

namespace FrameworkSample.Models;

public class RunInfo : ReactiveObject
{
    [Reactive]
    public int NumCycles { get; set; }
    
    [Reactive]
    public int NumLanes { get; set; }

    [Reactive] 
    public int NumTiles { get; set; } = 20;
    
    
    [Reactive] 
    public string CyclesError { get; set; } = string.Empty;

    [Reactive] 
    public string LanesError { get; set; } = string.Empty;


    public RunInfo()
    {
        // validation has been moved into the viewmodel because Avalonia's NumericUpDown control does
        // not support positioning of the error message.  This is a workaround to allow the error message
        // to be displayed in the correct location.
        this.WhenAnyValue(x => x.NumCycles)
            .Subscribe( cycles => CyclesError = (cycles is > 0 and <= 100) ? string.Empty : "Cycles must be between 1 and 100");
        this.WhenAnyValue(x => x.NumLanes)
            .Subscribe(lanes => LanesError = (lanes is > 0 and <= 8) ? string.Empty : "Lanes must be between 1 and 8");
    }

    public void Reset()
    {
        NumLanes = NumCycles = 0;
    }
}