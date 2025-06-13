using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using KJX.Core.Models;

namespace KJX.ProjectTemplate.Control.Converters;

public class NotificationTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (Enum.TryParse(value?.ToString(), out NotificationType result))
            return result;
        return NotificationType.Error;
    }
}

public class NotificationIsReadToFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasBeenRead = value != null && (bool)value;
        return hasBeenRead ? FontWeight.Normal : FontWeight.Bold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
