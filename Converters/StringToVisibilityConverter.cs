#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SoftwareProductsManager.Converters
{
    /// <summary>
    /// Конвертер для преобразования строки в видимость
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
