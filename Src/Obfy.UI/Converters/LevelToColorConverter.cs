using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Obfy.UI.Models;

namespace Obfy.UI.Converters;

/// <summary>
/// Converts a log level to a theme brush for display.
/// </summary>
public class LevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is LogLevel level
            ? level switch
            {
                LogLevel.Error => "SystemFillColorCriticalBrush",
                LogLevel.Warning => "SystemFillColorCautionBrush",
                LogLevel.Info => "SystemFillColorAttentionBrush",
                LogLevel.Debug => "TextFillColorSecondaryBrush",
                LogLevel.Success => "SystemFillColorSuccessBrush",
                _ => "TextFillColorPrimaryBrush"
            }
            : "TextFillColorPrimaryBrush";

        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
