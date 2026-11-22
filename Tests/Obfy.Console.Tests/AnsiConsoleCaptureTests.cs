using Shouldly;
using Spectre.Console;

namespace Obfy.Console.Tests;

/// <summary>
/// The capture is read as plain text (<c>Total Transformations\D+(\d+)</c> and similar), so it must hold no
/// escape codes on any machine. GitHub Actions sets <c>GITHUB_ACTIONS</c>, and Spectre's default profile
/// enrichers then turn ANSI on whatever <see cref="AnsiSupport.No"/> asks for: <c>ESC[0m</c> after the label
/// made the regex read 0 as the count, and two incremental-cache tests failed there while passing locally.
/// The project runs its collections serially (<c>xunit.runner.json</c>), so setting a process environment
/// variable here cannot reach another test.
/// </summary>
public class AnsiConsoleCaptureTests
{
    private const string Escape = "\u001b";

    private static string CaptureStyledLine(string? githubActions)
    {
        var previous = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", githubActions);
            using var capture = new AnsiConsoleCapture();
            AnsiConsole.MarkupLine("[bold]Total Transformations[/] [bold green]3[/]");
            return capture.Text;
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", previous);
        }
    }

    [Fact]
    public void Text_OnGitHubActions_HasNoEscapeCodes()
    {
        var text = CaptureStyledLine("true");

        text.ShouldNotContain(Escape);
        text.TrimEnd().ShouldBe("Total Transformations 3");
    }

    [Fact]
    public void Text_WithoutGitHubActions_HasNoEscapeCodes()
    {
        var text = CaptureStyledLine(null);

        text.ShouldNotContain(Escape);
        text.TrimEnd().ShouldBe("Total Transformations 3");
    }
}
