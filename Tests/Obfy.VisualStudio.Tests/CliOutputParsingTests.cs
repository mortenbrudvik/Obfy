using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// What the real CLI prints: a Metric/Value table (rounded box characters, or ASCII borders when stdout is
/// redirected to a non-Unicode code page) with "Types Renamed" and "Methods Renamed" rows, and failures
/// written to stdout as "Error: ..." lines.
/// </summary>
public class CliOutputParsingTests
{
    private const string UnicodeTable = """
        ╭───────────────────────┬──────────────────────────────╮
        │ Metric                │ Value                        │
        ├───────────────────────┼──────────────────────────────┤
        │ Status                │ Success                      │
        │ File                  │ App.dll                      │
        │ Output                │ C:\out\App2.obfuscated.dll   │
        │ Duration              │ 812ms                        │
        │ Strings Encrypted     │ 12                           │
        │ Resources Encrypted   │ 1                            │
        │ Types Renamed         │ 30                           │
        │ Methods Renamed       │ 120                          │
        │ Control Flow          │ 4                            │
        │ Protections           │ 2                            │
        │ Metadata Removed      │ 5                            │
        │ Total Transformations │ 174                          │
        ╰───────────────────────┴──────────────────────────────╯
        """;

    private const string AsciiTable = """
        +-----------------------------------------------+
        | Metric                | Value                 |
        |-----------------------+-----------------------|
        | Status                | Success               |
        | Strings Encrypted     | 12                    |
        | Types Renamed         | 30                    |
        | Methods Renamed       | 120                   |
        | Total Transformations | 174                   |
        +-----------------------------------------------+
        """;

    [Theory]
    [InlineData(UnicodeTable)]
    [InlineData(AsciiTable)]
    public void ParseStatistics_ReadsTheRowsTheCliActuallyPrints(string output)
    {
        var stats = CliArgumentBuilder.ParseStatistics(output);

        stats.StringsEncrypted.ShouldBe(12);
        stats.SymbolsRenamed.ShouldBe(150); // 30 types + 120 methods
        stats.TotalTransformations.ShouldBe(174);
    }

    [Fact]
    public void ParseStatistics_IgnoresDigitsInOtherRows()
    {
        var stats = CliArgumentBuilder.ParseStatistics(UnicodeTable);

        // "App2.obfuscated.dll" and "812ms" must not leak into any counter.
        stats.StringsEncrypted.ShouldBe(12);
    }

    [Fact]
    public void ExtractErrorMessage_PrefersStderr()
    {
        var message = CliArgumentBuilder.ExtractErrorMessage("Error: from stdout", "boom on stderr\n", 1);

        message.ShouldBe("boom on stderr");
    }

    [Fact]
    public void ExtractErrorMessage_FallsBackToTheErrorLinesTheCliWritesToStdout()
    {
        const string stdout = """
            Failed to obfuscate App.dll
            Error: Assembly has no metadata
            Obfuscation finished with errors.
            """;

        var message = CliArgumentBuilder.ExtractErrorMessage(stdout, "", 1);

        message.ShouldBe("Assembly has no metadata");
    }

    [Fact]
    public void ExtractErrorMessage_WithoutErrorLines_UsesTheLastStdoutLine()
    {
        const string stdout = "No assemblies found.\n\n";

        CliArgumentBuilder.ExtractErrorMessage(stdout, "", 1).ShouldBe("No assemblies found.");
    }

    [Fact]
    public void ExtractErrorMessage_WithNoOutputAtAll_ReportsTheExitCode()
    {
        CliArgumentBuilder.ExtractErrorMessage("", "  ", 7).ShouldBe("Process exited with code 7");
    }
}
