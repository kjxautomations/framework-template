using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Framework.Services;

/// <summary>
/// A simple notification service that stores its items in memory
/// The observer is assumed to be running in the UI thread, and all mutations
/// Are performed there.
/// The caller may set the HasBeenRead field and remove items. Technically,
/// it is safe to add items as well but this is not the intended use case
/// </summary>

public class InMemoryNotificationService : INotificationService
{
    public required ILogger<INotificationService> Logger { get; init; }

    public InMemoryNotificationService(SynchronizationContext context)
    {
        _executionContext = context;
        _records = new ObservableCollection<NotificationRecord>();
    }

    
    public ObservableCollection<NotificationRecord> Notifications => _records;

    public void AddNotification(NotificationType type, string message, string sourceFilePath = "", int sourceLineNumber = 0)
    {
        if (type == NotificationType.Exception)
            throw new ArgumentException("Exceptions must be added with the AddException method", nameof(type));
        Logger.LogInformation($"Notification: {type} {message} {sourceFilePath}:{sourceLineNumber}");
        var newNotification = new NotificationRecord
        {
            Message = message, 
            NotificationType = type, 
            SourceFileName = sourceFilePath,
            SourceLineNumber = sourceLineNumber,
            When = DateTime.Now
        };
        ExecuteOnSyncContext(() =>
        {
            _records.Add(newNotification);
        });
    }

    public void AddException(string message, Exception e, string sourceFilePath = "", int sourceLineNumber = 0)
    {
        if (null == e)
            throw new ArgumentNullException("AddException requires an exception object");
        Logger.LogError(e, $"Exception: {message} {sourceFilePath}:{sourceLineNumber}");
        var newNotification = new NotificationRecord
        {
            Message = message, 
            NotificationType = NotificationType.Exception, 
            SourceFileName = sourceFilePath,
            SourceLineNumber = sourceLineNumber,
            Exception = e,
            When = DateTime.Now
        };
        ExecuteOnSyncContext(() =>
        {
            _records.Add(newNotification);
        });
    }

    public void Remove(NotificationRecord notification)
    {
        ExecuteOnSyncContext(() => _records.Remove(notification));
    }
    private readonly ObservableCollection<NotificationRecord> _records;
    private readonly SynchronizationContext _executionContext;
    private readonly object _lock = new object();
    
    private void ExecuteOnSyncContext(Action action)
    {
        if (SynchronizationContext.Current == _executionContext)
        {
            // this lock is used primarily for unit testing, since we don't have a UI thread
            lock (_lock)
            {
                action();
            }
        }
        else
        {
            _executionContext.Post(_ => action(), null);
        }
    }
}