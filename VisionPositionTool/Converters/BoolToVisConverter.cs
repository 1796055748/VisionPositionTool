using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionPositionTool.Converters
{
    /// <summary>bool → Visibility；ConverterParameter="invert" 时反相</summary>
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool && (bool)value;
            if (parameter is string s && s == "invert") b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
