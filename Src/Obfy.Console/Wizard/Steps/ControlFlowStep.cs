using Obfy.Core.Models;
using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to configure control flow obfuscation.
/// </summary>
public class ControlFlowStep : WizardStep
{
    public override string Title => "Control Flow";

    public override Task ExecuteAsync(WizardContext context)
    {
        // Skip in quick mode
        if (context.IsQuickMode)
        {
            return Task.CompletedTask;
        }

        WriteHeader(Title);

        WriteHint("Control flow obfuscation transforms method logic into complex patterns.");
        WriteHint("This makes code harder to understand but may impact performance.");
        AnsiConsole.WriteLine();

        var enabled = AnsiConsole.Confirm(
            "Enable control flow obfuscation?",
            defaultValue: context.Settings.ControlFlow.Enabled);

        context.Settings.ControlFlow.Enabled = enabled;

        if (enabled)
        {
            AnsiConsole.WriteLine();

            // Mode selection
            var mode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Control flow mode:")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices([
                        "Switch          - State machine transformation",
                        "OpaquePredicate - Confusing conditional branches",
                        "Combined        - Both techniques (maximum protection)"
                    ]));

            context.Settings.ControlFlow.Mode = mode switch
            {
                var m when m.StartsWith("Switch") => ControlFlowMode.Switch,
                var m when m.StartsWith("Opaque") => ControlFlowMode.OpaquePredicate,
                _ => ControlFlowMode.Combined
            };

            // Intensity
            AnsiConsole.WriteLine();
            var intensity = AnsiConsole.Prompt(
                new TextPrompt<int>("Intensity (0-100):")
                    .DefaultValue(context.Settings.ControlFlow.Intensity)
                    .Validate(value => value switch
                    {
                        < 0 => ValidationResult.Error("[red]Must be 0 or greater[/]"),
                        > 100 => ValidationResult.Error("[red]Must be 100 or less[/]"),
                        _ => ValidationResult.Success()
                    }));

            context.Settings.ControlFlow.Intensity = intensity;

            WriteInfo($"Higher intensity = stronger protection but slower runtime.");
        }

        return Task.CompletedTask;
    }
}
