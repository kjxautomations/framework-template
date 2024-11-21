using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Framework.Services;

namespace ProjectTemplate.Converters;

public class NotificationTypeToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Enum.TryParse(value.ToString(), out NotificationType result))
            return result;
        return NotificationType.Error;
    }
}

public class NotificationIsReadToFontStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasBeenRead = (bool)value;
        return hasBeenRead ? FontWeight.Normal : FontWeight.Bold;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
