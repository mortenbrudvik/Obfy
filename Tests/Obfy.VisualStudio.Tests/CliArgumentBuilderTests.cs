using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class CliArgumentBuilderTests
{
    [Fact]
    public void Build_PassesConfigFileAsArgv_WithoutTechniqueFlags()
    {
        var args = CliArgumentBuilder.Build(@"C:\src\My App.dll", @"C:\out\My App.dll", @"C:\src\obfy.json");
        args.ShouldBe(new[]
        {
            @"C:\src\My App.dll",
            "-o",
            @"C:\out",
            "-c",
            @"C:\src\obfy.json"
        });
        args.ShouldNotContain("-l");
        args.ShouldNotContain("--anti-debug");

        var commandLine = CliArgumentBuilder.ToCommandLine(args);
        commandLine.ShouldContain("\"C:\\src\\My App.dll\"");
        commandLine.ShouldContain("-c \"C:\\src\\obfy.json\"");
    }

    [Fact]
    public void Build_WithoutConfig_FallsBackToLevel()
    {
        var args = CliArgumentBuilder.Build(@"D:\a.dll", null, configPath: null, level: ObfuscationLevel.Standard, generateSymbolMap: true);
        args.ShouldContain("-l");
        args.ShouldContain("standard");
        args.ShouldContain("--map");
        args.ShouldContain(@"D:\a.map.json");
        args.ShouldNotContain("-c");
    }

    [Fact]
    public void Quote_EscapesEmbeddedQuotes()
    {
        CliArgumentBuilder.Quote(@"C:\src\My ""App"".dll").ShouldBe("\"C:\\src\\My \\\"App\\\".dll\"");
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
