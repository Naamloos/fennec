using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Converters
{
    public class SubstringConverter : IValueConverter
    {
        private readonly int _length;
        private readonly int _start;

        public SubstringConverter(int length, int start)
        {
            _length = length;
            _start = start;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text ||
                _start < 0 ||
                _start >= text.Length)
            {
                return "?";
            }

            var length = Math.Min(_length, text.Length - _start);
            return text.Substring(_start, length);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
