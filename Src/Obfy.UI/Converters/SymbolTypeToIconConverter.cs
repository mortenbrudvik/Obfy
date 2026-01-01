using System.Globalization;
using System.Windows.Data;
using Obfy.UI.Models;
using Wpf.Ui.Controls;

namespace Obfy.UI.Converters;

/// <summary>
/// Converts a symbol type to a SymbolRegular icon.
/// </summary>
public class SymbolTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SymbolType symbolType)
        {
            return symbolType switch
            {
                SymbolType.Namespace => SymbolRegular.FolderOpen24,
                SymbolType.Type => SymbolRegular.Code24,
                SymbolType.Method => SymbolRegular.Cube24,
                SymbolType.Field => SymbolRegular.TextBulletListSquare24,
                SymbolType.Property => SymbolRegular.Wrench24,
                SymbolType.Parameter => SymbolRegular.TextDescription24,
                _ => SymbolRegular.Document24
            };
        }
        return SymbolRegular.Document24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
