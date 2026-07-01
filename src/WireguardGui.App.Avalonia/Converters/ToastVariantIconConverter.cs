using System.Globalization;
using Avalonia.Data.Converters;
using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Avalonia.Converters;

internal sealed class ToastVariantIconConverter : IValueConverter
{
    public static ToastVariantIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AppToastVariant.Success => "✓",
            AppToastVariant.Error => "!",
            _ => "i",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
