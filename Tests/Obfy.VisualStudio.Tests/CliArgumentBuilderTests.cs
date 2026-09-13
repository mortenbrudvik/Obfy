using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class CliArgumentBuilderTests
{
    [Fact]
    public void Build_QuotesPaths_AndIncludesLevel()
    {
        var args = CliArgumentBuilder.Build(@"C:\src\My App.dll", @"C:\out\My App.dll", ObfySettings.ForLevel(ObfuscationLevel.Standard));
        args.ShouldContain("\"C:\\src\\My App.dll\"");
        args.ShouldContain("-o \"C:\\out\"");
        args.ShouldContain("-l standard");
        args.ShouldNotContain("--anti-debug");
    }

    [Fact]
    public void Build_CustomLevel_EmitsTogglesAndOptionalMap()
    {
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = false,
            SymbolRenaming = false,
            ControlFlow = true,
            AntiDump = true,
            ConstantEncryption = true
        };

        var args = CliArgumentBuilder.Build(@"D:\a.dll", null, settings, generateSymbolMap: true);
        args.ShouldContain("--no-string-encryption");
        args.ShouldContain("--no-symbol-renaming");
        args.ShouldContain("--control-flow");
        args.ShouldContain("--anti-dump");
        args.ShouldContain("--encrypt-constants");
        args.ShouldContain("--map \"D:\\a.map.json\"");
    }

    [Fact]
    public void ParseStatistics_ReadsCliSummaryLines()
    {
        var stats = CliArgumentBuilder.ParseStatistics("Strings encrypted: 4\nSymbols renamed: 9\n");
        stats.StringsEncrypted.ShouldBe(4);
        stats.SymbolsRenamed.ShouldBe(9);
        stats.TotalTransformations.ShouldBe(13);
    }
}
