using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Obfy.UI.Converters;

/// <summary>
/// Converts <c>IsSourceFile</c> to a Fluent icon.
/// </summary>
public class FileKindToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? SymbolRegular.Code24 : SymbolRegular.Box24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
