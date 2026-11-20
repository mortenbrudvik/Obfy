using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Obfy.UI.Models;

namespace Obfy.UI.Converters;

/// <summary>
/// Maps a file status to a theme brush. Glyphs stay distinct; color adds success, error, and caution.
/// </summary>
public class FileStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is FileStatus status
            ? status switch
            {
                FileStatus.Success => "SystemFillColorSuccessBrush",
                FileStatus.Error => "SystemFillColorCriticalBrush",
                FileStatus.Skipped => "SystemFillColorCautionBrush",
                FileStatus.Processing => "SystemFillColorAttentionBrush",
                _ => "TextFillColorPrimaryBrush"
            }
            : "TextFillColorPrimaryBrush";

        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;

        if (Application.Current?.TryFindResource("TextFillColorPrimaryBrush") is Brush primary)
            return primary;

        return SystemColors.ControlTextBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
