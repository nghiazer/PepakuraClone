using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FourHUnfolder.App.Converters;

/// <summary>
/// Converts a "#RRGGBB" or "#AARRGGBB" hex string to a <see cref="SolidColorBrush"/>.
/// Returns <see cref="Brushes.Transparent"/> for invalid / empty input.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class HexColorBrushConverter : IValueConverter
{
    public static readonly HexColorBrushConverter Instance = new();

    public object Convert(object? value, Type targetType,
                          object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch { /* fall through */ }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType,
                              object? parameter, CultureInfo culture) =>
        value is SolidColorBrush b ? b.Color.ToString() : "#000000";
}
