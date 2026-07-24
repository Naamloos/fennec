using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dev.Naamloos.Fennec.App.Converters
{
    public sealed class FirstNonNullConverter : IMultiValueConverter
    {
        public object? Convert(
            object[] values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            return values[0] is string custom
                ? custom
                : values[1];
        }

        public object[] ConvertBack(
            object? value,
            Type[] targetTypes,
            object? parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
