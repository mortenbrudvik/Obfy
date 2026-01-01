using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Obfy.UI.Models;

namespace Obfy.UI.Converters;

/// <summary>
/// Converts a log level to a color brush for display.
/// </summary>
public class LevelToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xFF, 0xAA, 0x00));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0x00, 0xAA, 0xFF));
    private static readonly SolidColorBrush DebugBrush = new(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x44, 0xFF, 0x44));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));

    static LevelToColorConverter()
    {
        ErrorBrush.Freeze();
        WarningBrush.Freeze();
        InfoBrush.Freeze();
        DebugBrush.Freeze();
        SuccessBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => ErrorBrush,
                LogLevel.Warning => WarningBrush,
                LogLevel.Info => InfoBrush,
                LogLevel.Debug => DebugBrush,
                LogLevel.Success => SuccessBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
