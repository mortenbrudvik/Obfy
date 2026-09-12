using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Obfy.UI.Converters;

/// <summary>
/// Maps true to a 1* grid row and false to a zero-height row.
/// </summary>
public class BoolToStarGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
