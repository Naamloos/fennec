using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters;

public sealed class BytesToImageSourceConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is byte[] bytes ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => Binding.DoNothing;
}
