using System.Globalization;
using System.Windows.Data;
using Obfy.UI.Models;
using Wpf.Ui.Controls;

namespace Obfy.UI.Converters;

/// <summary>
/// Converts a file processing status to a Fluent icon.
/// </summary>
public class FileStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is FileStatus status
            ? status switch
            {
                FileStatus.Success => SymbolRegular.CheckmarkCircle24,
                FileStatus.Error => SymbolRegular.ErrorCircle24,
                FileStatus.Processing => SymbolRegular.ArrowSync24,
                _ => SymbolRegular.Document24
            }
            : SymbolRegular.Document24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
