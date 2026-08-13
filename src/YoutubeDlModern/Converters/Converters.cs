using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using YoutubeDlModern.Models;

namespace YoutubeDlModern.Converters;

/// <summary>true -> false and vice versa.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// null/empty -> Visible by default; pass "invert" to flip (non-null -> Visible).
/// </summary>
public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var show = invert ? !isNull : isNull;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a DownloadStatus to a theme brush for the status chip.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is DownloadStatus status ? status switch
        {
            DownloadStatus.Completed => "SuccessBrush",
            DownloadStatus.Failed => "ErrorBrush",
            DownloadStatus.Canceled => "TextMutedBrush",
            DownloadStatus.Paused => "WarningBrush",
            DownloadStatus.Downloading or DownloadStatus.Processing => "InfoBrush",
            _ => "TextSecondaryBrush" // Queued
        } : "TextSecondaryBrush";

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>false -> Visible, true -> Collapsed.</summary>
public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a double 0-100 progress as "42%".</summary>
public sealed class ProgressTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && d > 0 ? $"{d:0}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
