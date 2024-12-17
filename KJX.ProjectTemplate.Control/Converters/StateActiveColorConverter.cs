using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KJX.ProjectTemplate.Control.Converters;

public class StateActiveColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if ((bool)value)
        {
            {
                return new SolidColorBrush(Colors.GreenYellow);
            }
        }
        return new SolidColorBrush(Colors.DarkGray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

}