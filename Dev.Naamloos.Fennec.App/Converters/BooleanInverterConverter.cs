using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters;

public sealed class BooleanInverterConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is bool boolean && !boolean;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => Binding.DoNothing;
}
