using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to select quick or advanced mode.
/// </summary>
public class ModeSelectionStep : WizardStep
{
    public override string Title => "Setup Mode";

    public override Task ExecuteAsync(WizardContext context)
    {
        WriteHeader(Title);

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to configure obfuscation?")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices([
                    "Quick Setup - Select a preset level (recommended)",
                    "Advanced Setup - Configure each option individually"
                ]));

        context.IsQuickMode = mode.StartsWith("Quick");

        return Task.CompletedTask;
    }
}
