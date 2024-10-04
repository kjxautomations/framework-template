using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Devices.Utils;

using Framework.Core.ViewModels;
using Framework.Services;
using FrameworkSample.Models;
using FrameworkSample.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace FrameworkSample.ViewModels;

public class FinishScreenViewModel : StateViewModelBase<NavigationStates,NavigationTriggers>
{
    [Reactive] public string StatusMessage { get; private set; }

    public FinishScreenViewModel(INavigationService<NavigationStates, NavigationTriggers> navigationService,
        SequencingService sequencingService,
        ILogger<FinishScreenViewModel> logger)
        : base(navigationService, new[] { NavigationStates.SequencingComplete, NavigationStates.SequencingAborted })
    {
        this.WhenAnyValue(x => x.CurrentState)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe((s) =>
            {
                if (s == NavigationStates.SequencingAborted)
                {
                    StatusMessage = "Sequencing aborted. See the notification log for details.";
                    logger.LogInformation($"SS state is {sequencingService.State}");
                }
            });
        sequencingService.WhenAnyValue(x => x.State)
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(s =>
            {
                logger.LogInformation($"SS state is {s} overall {CurrentState}");
                if (CurrentState != NavigationStates.SequencingAborted)
                {
                    StatusMessage = s switch
                    {
                        SequencingState.Idle => "Sequencing idle",
                        SequencingState.Running => "Sequencing in progress",
                        SequencingState.Cancelling => "Sequencing being cancelled",
                        SequencingState.Cancelled => "Sequencing cancelled",
                        SequencingState.Complete => "Sequencing complete",
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }
            });
    }
    
}