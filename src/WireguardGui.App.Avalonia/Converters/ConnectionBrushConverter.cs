using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WireguardGui.App.Avalonia.Converters;

internal sealed class ConnectionBrushConverter : IValueConverter
{
    public static readonly ConnectionBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var connected = value is true;
        var key = connected ? "SuccessBrush" : "DangerBrush";
        var app = global::Avalonia.Application.Current;
        if (app?.Resources.TryGetValue(key, out var resource) == true && resource is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
