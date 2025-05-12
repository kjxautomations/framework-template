using System.Collections.ObjectModel;
using KJX.Core.Models;

namespace KJX.Core.Interfaces;

/// <summary>
/// Interface for the notification service used to display and notify the user of an application event.
/// </summary>
public interface INotificationService 
{
    public ObservableCollection<NotificationRecord> Notifications { get; }

    public void AddNotification(NotificationType type, string message,
        [System.Runtime.CompilerServices.CallerFilePath]
        string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber]
        int sourceLineNumber = 0);
    public void AddException(string message, Exception e,
        [System.Runtime.CompilerServices.CallerFilePath]
        string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber]
        int sourceLineNumber = 0);

    public void Remove(NotificationRecord notification);
}