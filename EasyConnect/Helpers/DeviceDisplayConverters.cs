using System.Globalization;
using System.Windows.Data;
using EasyConnect.Models;

namespace EasyConnect.Helpers;

public sealed class BatteryTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int percentage and >= 0 and <= 100 ? $"{percentage}%" : "--";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ConnectionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DeviceConnectionStatus.Connected => "Connected",
        DeviceConnectionStatus.Disconnected => "Disconnected",
        _ => "Status unavailable"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DeviceIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var category = (value as string ?? string.Empty).ToLowerInvariant();
        if (category.Contains("head") || category.Contains("audio")) return "\uE7F6";
        if (category.Contains("mouse")) return "\uE962";
        if (category.Contains("keyboard")) return "\uE765";
        if (category.Contains("phone")) return "\uE8EA";
        if (category.Contains("computer")) return "\uE7F4";
        if (category.Contains("game")) return "\uE7FC";
        return "\uE702";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
