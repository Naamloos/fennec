using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dev.Naamloos.Fennec.App.Converters
{
    public class NotNullConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is not null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
