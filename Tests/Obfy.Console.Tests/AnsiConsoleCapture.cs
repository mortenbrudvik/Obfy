using Spectre.Console;

namespace Obfy.Console.Tests;

/// <summary>
/// Redirects <see cref="AnsiConsole"/> into a string for the life of the instance, wide enough that a long
/// path in a table cell is not wrapped, and restores the previous console on dispose. The test project runs
/// its collections serially, so swapping the static console is safe.
/// </summary>
internal sealed class AnsiConsoleCapture : IDisposable
{
    private readonly IAnsiConsole _previous = AnsiConsole.Console;
    private readonly StringWriter _writer = new();

    public AnsiConsoleCapture()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(_writer)
        });
        console.Profile.Width = 1000;
        AnsiConsole.Console = console;
    }

    public string Text => _writer.ToString();

    public void Dispose() => AnsiConsole.Console = _previous;
}
