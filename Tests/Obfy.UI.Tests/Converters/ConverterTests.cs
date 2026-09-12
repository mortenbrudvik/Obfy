using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Obfy.Core.Models;
using Obfy.UI.Converters;
using Obfy.UI.Models;
using Shouldly;
using Wpf.Ui.Controls;

namespace Obfy.UI.Tests.Converters;

public class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void InverseBooleanConverter_InvertsBool()
    {
        var converter = new InverseBooleanConverter();

        converter.Convert(true, typeof(bool), null!, Culture).ShouldBe(false);
        converter.Convert(false, typeof(bool), null!, Culture).ShouldBe(true);
    }

    [Fact]
    public void InverseBooleanConverter_NonBool_ReturnsFalse()
    {
        var converter = new InverseBooleanConverter();

        converter.Convert("nope", typeof(bool), null!, Culture).ShouldBe(false);
        converter.Convert(null!, typeof(bool), null!, Culture).ShouldBe(false);
    }

    [Fact]
    public void BooleanToVisibilityConverter_MapsValues()
    {
        var converter = new BooleanToVisibilityConverter();

        converter.Convert(true, typeof(Visibility), null!, Culture).ShouldBe(Visibility.Visible);
        converter.Convert(false, typeof(Visibility), null!, Culture).ShouldBe(Visibility.Collapsed);
        converter.Convert(null!, typeof(Visibility), null!, Culture).ShouldBe(Visibility.Collapsed);
    }

    [Fact]
    public void EnumDescriptionConverter_UsesDescriptionAttribute()
    {
        var converter = new EnumDescriptionConverter();

        converter.Convert(ObfuscationLevel.Minimal, typeof(string), null!, Culture)
            .ShouldBe("Minimal — symbol renaming only");
        converter.Convert(EncryptionAlgorithm.Aes256, typeof(string), null!, Culture)
            .ShouldBe("AES-256");
    }

    [Fact]
    public void SymbolTypeToIconConverter_MapsKnownTypes()
    {
        var converter = new SymbolTypeToIconConverter();

        converter.Convert(SymbolType.Method, typeof(SymbolRegular), null!, Culture)
            .ShouldBe(SymbolRegular.Cube24);
        converter.Convert("not a type", typeof(SymbolRegular), null!, Culture)
            .ShouldBe(SymbolRegular.Document24);
    }

    [Fact]
    public void FileStatusToIconConverter_MapsStatuses()
    {
        var converter = new FileStatusToIconConverter();

        converter.Convert(FileStatus.Success, typeof(SymbolRegular), null!, Culture)
            .ShouldBe(SymbolRegular.CheckmarkCircle24);
        converter.Convert(FileStatus.Error, typeof(SymbolRegular), null!, Culture)
            .ShouldBe(SymbolRegular.ErrorCircle24);
        converter.Convert(FileStatus.Processing, typeof(SymbolRegular), null!, Culture)
            .ShouldBe(SymbolRegular.ArrowSync24);
    }

    [Fact]
    public void BoolToStarGridLengthConverter_ReturnsStarOrZero()
    {
        var converter = new BoolToStarGridLengthConverter();

        var visible = converter.Convert(true, typeof(GridLength), null!, Culture).ShouldBeOfType<GridLength>();
        visible.GridUnitType.ShouldBe(GridUnitType.Star);
        visible.Value.ShouldBe(1);

        var hidden = converter.Convert(false, typeof(GridLength), null!, Culture).ShouldBeOfType<GridLength>();
        hidden.Value.ShouldBe(0);
    }
}
