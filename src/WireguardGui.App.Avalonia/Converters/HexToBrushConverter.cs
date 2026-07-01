using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WireguardGui.App.Avalonia.Converters;

internal sealed class HexToBrushConverter : IValueConverter
{
    public static HexToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        return new SolidColorBrush(Color.Parse(hex));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
