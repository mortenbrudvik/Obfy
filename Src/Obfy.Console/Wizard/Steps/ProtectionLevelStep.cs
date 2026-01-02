using Obfy.Core.Models;
using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to select the protection level.
/// </summary>
public class ProtectionLevelStep : WizardStep
{
    public override string Title => "Protection Level";

    public override Task ExecuteAsync(WizardContext context)
    {
        WriteHeader(Title);

        // Suggest level based on use case
        var suggested = context.UseCase switch
        {
            "Class Library / NuGet Package" => "Minimal",
            "Game (Unity)" => "Aggressive",
            _ => "Standard"
        };

        WriteHint($"Based on your application type, we suggest: [cyan]{suggested}[/]");
        AnsiConsole.WriteLine();

        var level = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select your protection level:")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices([
                    "Minimal    - Symbol renaming only (fastest builds)",
                    "Standard   - Strings + symbols (recommended for most apps)",
                    "Aggressive - Maximum protection (all features enabled)"
                ]));

        var parsedLevel = level switch
        {
            var l when l.StartsWith("Minimal") => ObfuscationLevel.Minimal,
            var l when l.StartsWith("Aggressive") => ObfuscationLevel.Aggressive,
            _ => ObfuscationLevel.Standard
        };

        // Create settings from level preset
        context.Settings = ObfySettings.ForLevel(parsedLevel);

        // Show what's enabled
        AnsiConsole.WriteLine();
        ShowLevelDetails(parsedLevel);

        return Task.CompletedTask;
    }

    private static void ShowLevelDetails(ObfuscationLevel level)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("")
            .AddColumn("");

        switch (level)
        {
            case ObfuscationLevel.Minimal:
                table.AddRow("[dim]Enabled:[/]", "Symbol renaming, Metadata removal");
                table.AddRow("[dim]Disabled:[/]", "String encryption, Control flow, Anti-debug");
                break;

            case ObfuscationLevel.Standard:
                table.AddRow("[dim]Enabled:[/]", "String encryption, Symbol renaming, Metadata removal");
                table.AddRow("[dim]Disabled:[/]", "Control flow, Anti-debug, Anti-tamper");
                break;

            case ObfuscationLevel.Aggressive:
                table.AddRow("[dim]Enabled:[/]", "All protections (strings, control flow, symbols,");
                table.AddRow("", "anti-debug, anti-tamper, anti-decompiler, resources, constants)");
                break;
        }

        AnsiConsole.Write(table);
    }
}
