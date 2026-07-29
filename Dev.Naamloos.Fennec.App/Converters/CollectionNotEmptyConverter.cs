using System.Collections;
using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters;

public sealed class CollectionNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ICollection collection && collection.Count > 0;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => Binding.DoNothing;
}
