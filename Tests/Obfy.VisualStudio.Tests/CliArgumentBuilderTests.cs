using Obfy.VisualStudio.Services;
using Shouldly;

// ProjectSupport tests live here too.

namespace Obfy.VisualStudio.Tests;

public class CliArgumentBuilderTests
{
    [Fact]
    public void Build_QuotesPaths_AndPassesConfigFile()
    {
        var args = CliArgumentBuilder.Build(@"C:\src\My App.dll", @"C:\out\My App.dll", @"C:\src\obfy.json");
        args.ShouldContain("\"C:\\src\\My App.dll\"");
        args.ShouldContain("-o \"C:\\out\"");
        args.ShouldContain("-c \"C:\\src\\obfy.json\"");
        args.ShouldNotContain("-l ");
        args.ShouldNotContain("--anti-debug");
    }

    [Fact]
    public void Build_WithoutConfig_FallsBackToLevel()
    {
        var args = CliArgumentBuilder.Build(@"D:\a.dll", null, configPath: null, level: ObfuscationLevel.Standard, generateSymbolMap: true);
        args.ShouldContain("-l standard");
        args.ShouldContain("--map \"D:\\a.map.json\"");
        args.ShouldNotContain("-c ");
    }

    [Theory]
    [InlineData(@"C:\src\App.csproj", true)]
    [InlineData(@"C:\src\Lib.vbproj", true)]
    [InlineData(@"C:\src\Native.vcxproj", false)]
    [InlineData(null, false)]
    public void ProjectSupport_IsSupportedProjectPath(string? path, bool expected)
    {
        ProjectSupport.IsSupportedProjectPath(path).ShouldBe(expected);
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
