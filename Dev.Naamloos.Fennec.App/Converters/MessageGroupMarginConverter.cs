using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters;

public sealed class MessageGroupMarginConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => value is true
            ? new Thickness(12, 8, 12, 1)
            : new Thickness(12, 1, 12, 1);

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => Binding.DoNothing;
}
