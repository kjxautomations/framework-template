using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KJX.ProjectTemplate.Services;
public enum NotificationType
{
    Info,
    Warning,
    Error,
    Exception
}
public class NotificationRecord : INotifyPropertyChanged
{
    private bool _hasBeenRead;
    public required int SourceLineNumber { get; init; }
    public required string SourceFileName { get; init; }
    public required string Message { get; init; }
    public required NotificationType NotificationType { get; init; }
    public required DateTime When { get; init; }
    public Exception? Exception { get; init; }

    public bool HasBeenRead
    {
        get => _hasBeenRead;
        set => SetField(ref _hasBeenRead, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}

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