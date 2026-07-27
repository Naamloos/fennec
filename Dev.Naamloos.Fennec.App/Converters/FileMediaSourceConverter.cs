using CommunityToolkit.Maui.Views;
using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters;

public sealed class FileMediaSourceConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => value is string path
            ? MediaSource.FromFile(path)
            : null;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => Binding.DoNothing;
}
