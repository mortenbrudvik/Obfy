using System.IO;
using Logging.Core.Configuration;
using Shouldly;

namespace Obfy.UI.Tests;

public class ReviewFixesWave4UiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveExplorerLogDirectory_WhenUnpackaged_ReturnsLogDirectory(string? packageFamilyName)
    {
        var probed = false;

        var resolved = LoggingConfiguration.ResolveExplorerLogDirectory(
            packageFamilyName,
            @"C:\Users\Test\AppData\Local",
            _ =>
            {
                probed = true;
                return true;
            });

        resolved.ShouldBe(LoggingConfiguration.LogDirectory);
        probed.ShouldBeFalse();
    }

    [Fact]
    public void ResolveExplorerLogDirectory_WhenPackagedAndVirtualizedDirectoryExists_ReturnsVirtualizedDirectory()
    {
        const string localAppData = @"C:\Users\Test\AppData\Local";
        const string packageFamilyName = "MortenBrudvik.Obfy_abc123";
        var expected = Path.Combine(
            localAppData,
            "Packages",
            packageFamilyName,
            "LocalCache",
            "Local",
            "Obfy",
            "Logs");

        string? probedPath = null;
        var resolved = LoggingConfiguration.ResolveExplorerLogDirectory(
            packageFamilyName,
            localAppData,
            path =>
            {
                probedPath = path;
                return string.Equals(path, expected, StringComparison.Ordinal);
            });

        probedPath.ShouldBe(expected);
        resolved.ShouldBe(expected);
    }

    [Fact]
    public void ResolveExplorerLogDirectory_WhenPackagedAndVirtualizedDirectoryMissing_FallsBackToLogDirectory()
    {
        const string localAppData = @"C:\Users\Test\AppData\Local";
        const string packageFamilyName = "MortenBrudvik.Obfy_abc123";
        var expectedVirtualized = Path.Combine(
            localAppData,
            "Packages",
            packageFamilyName,
            "LocalCache",
            "Local",
            "Obfy",
            "Logs");

        string? probedPath = null;
        var resolved = LoggingConfiguration.ResolveExplorerLogDirectory(
            packageFamilyName,
            localAppData,
            path =>
            {
                probedPath = path;
                return false;
            });

        probedPath.ShouldBe(expectedVirtualized);
        resolved.ShouldBe(LoggingConfiguration.LogDirectory);
    }

    [Fact]
    public void ResolveExplorerLogDirectory_LiveUnpackagedRunner_ReturnsLogDirectoryWithoutThrowing()
    {
        var expectedDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Obfy",
            "Logs");

        LoggingConfiguration.LogDirectory.ShouldBe(expectedDefault);
        LoggingConfiguration.ResolveExplorerLogDirectory().ShouldBe(LoggingConfiguration.LogDirectory);
    }
}
