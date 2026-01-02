using Spectre.Console;

namespace Obfy.Console.Wizard;

/// <summary>
/// Base class for wizard steps.
/// </summary>
public abstract class WizardStep
{
    /// <summary>
    /// The title of this step.
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// Execute this step.
    /// </summary>
    public abstract Task ExecuteAsync(WizardContext context);

    /// <summary>
    /// Write a section header.
    /// </summary>
    protected void WriteHeader(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[cyan]{title}[/]").LeftJustified());
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Write a hint in dim text.
    /// </summary>
    protected void WriteHint(string hint)
    {
        AnsiConsole.MarkupLine($"[dim]{hint}[/]");
    }

    /// <summary>
    /// Write an info message.
    /// </summary>
    protected void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]i[/] {message}");
    }
}
