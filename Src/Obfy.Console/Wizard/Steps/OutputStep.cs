using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Final step to review and generate the configuration.
/// </summary>
public class OutputStep : WizardStep
{
    public override string Title => "Generate Configuration";

    public override async Task ExecuteAsync(WizardContext context)
    {
        // Display summary
        ConfigurationWizard.DisplaySummary(context);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"Output file: [cyan]{context.OutputFile.FullName}[/]");
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices([
                    "Generate configuration file",
                    "Cancel"
                ]));

        if (action.StartsWith("Cancel"))
        {
            context.Cancelled = true;
            return;
        }

        var written = await ConfigurationWizard.GenerateConfigAsync(context).ConfigureAwait(false);
        if (!written)
        {
            context.Cancelled = true;
        }

        return;
    }
}
