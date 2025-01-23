using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using KJX.ProjectTemplate.Control.Views;
using KJX.Core.Interfaces;
using KJX.Core.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace KJX.ProjectTemplate.Control.ViewModels;

public class NotificationsViewModel : ViewModelBase
{
    [Reactive] public ReactiveCommand<Unit, Unit> ShowNotificationsCommand { get; set; }
    
    public NotificationsViewModel(INotificationService notificationService)
    {
        NotificationService = notificationService;
        notificationService.Notifications.CollectionChanged += RecordsOnCollectionChanged;
        foreach (NotificationRecord record in notificationService.Notifications)
            record.PropertyChanged += NotificationOnPropertyChanged;
        ShowNotificationsCommand = ReactiveCommand.Create(ShowNotifications);
        
        UpdateCounters();
    }

    public void ShowNotifications()
    {
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var notifications = new NotificationsWindow { DataContext = this };
            notifications.ShowDialog(desktop.MainWindow);
        }
        else if (Application.Current.ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var notifications = new NotificationsWindowControl { DataContext = this };

            var existingMain = singleViewPlatform.MainView;
            notifications.Close += () =>
            {
                singleViewPlatform.MainView = existingMain;
            };
            singleViewPlatform.MainView = notifications;
        }
    }
    
    private void RecordsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (NotificationRecord removed in e.OldItems)
                removed.PropertyChanged -= NotificationOnPropertyChanged;
        if (e.NewItems != null)
            foreach (NotificationRecord added in e.NewItems)
                added.PropertyChanged += NotificationOnPropertyChanged;
        UpdateCounters();
    }
    private void NotificationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotificationRecord.HasBeenRead))
            return;
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        int unread = 0, errors = 0, warnings = 0, infos = 0;
        foreach (var record in NotificationService.Notifications)
        {
            switch (record.NotificationType)
            {
                case NotificationType.Error:
                case NotificationType.Exception:
                    errors++;
                    break;
                case NotificationType.Info:
                    infos++;
                    break;
                case NotificationType.Warning:
                    warnings++;
                    break;
            }

            if (!record.HasBeenRead)
                unread++;
        }
        UnreadNotifications = unread;
        Warnings = warnings;
        Infos = infos;
        ErrorsAndExceptions = errors;
    }

    [Reactive]
    public int UnreadNotifications { get; private set; }

    [Reactive] public int ErrorsAndExceptions { get; private set; }
    
    [Reactive]
    public int Warnings { get; private set; }

    [Reactive] 
    public int Infos { get; private set; }

    public required INotificationService NotificationService { get; init; }
}