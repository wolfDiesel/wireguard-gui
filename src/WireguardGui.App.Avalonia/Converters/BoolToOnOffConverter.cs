using System.Globalization;
using Avalonia.Data.Converters;
using WireguardGui.App.Avalonia.Localization;

namespace WireguardGui.App.Avalonia.Converters;

internal sealed class BoolToOnOffConverter : IValueConverter
{
    public static BoolToOnOffConverter Instance { get; } = new();

    public LocalizationService? Localization { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = Localization?.Get("On") ?? "On";
        var off = Localization?.Get("Off") ?? "Off";
        return value is true ? on : off;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
