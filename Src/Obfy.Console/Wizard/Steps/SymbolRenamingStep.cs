using Obfy.Core.Models;
using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to configure symbol renaming.
/// </summary>
public class SymbolRenamingStep : WizardStep
{
    public override string Title => "Symbol Renaming";

    public override Task ExecuteAsync(WizardContext context)
    {
        // Skip in quick mode
        if (context.IsQuickMode)
        {
            return Task.CompletedTask;
        }

        WriteHeader(Title);

        WriteHint("Symbol renaming replaces type, method, and field names with obfuscated ones.");
        AnsiConsole.WriteLine();

        var enabled = AnsiConsole.Confirm(
            "Enable symbol renaming?",
            defaultValue: context.Settings.SymbolRenaming.Enabled);

        context.Settings.SymbolRenaming.Enabled = enabled;

        if (enabled)
        {
            AnsiConsole.WriteLine();

            // Naming mode
            var mode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Naming style:")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices([
                        "Unreadable  - Unicode characters (most confusing)",
                        "Sequential  - a, b, c, ... (smallest size)",
                        "Hash        - Hash-based names (consistent)",
                        "Random      - Random characters"
                    ]));

            context.Settings.SymbolRenaming.Mode = mode switch
            {
                var m when m.StartsWith("Unreadable") => NamingMode.Unreadable,
                var m when m.StartsWith("Sequential") => NamingMode.Sequential,
                var m when m.StartsWith("Hash") => NamingMode.Hash,
                _ => NamingMode.Random
            };

            // What to rename
            AnsiConsole.WriteLine();
            var choices = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("What symbols should be renamed?")
                    .InstructionsText("[dim](Press [cyan]<space>[/] to toggle, [cyan]<enter>[/] to confirm)[/]")
                    .AddChoices([
                        "Types (classes, structs, interfaces)",
                        "Methods",
                        "Properties",
                        "Fields",
                        "Parameters"
                    ])
                    .Select("Types (classes, structs, interfaces)")
                    .Select("Methods")
                    .Select("Properties")
                    .Select("Fields")
                    .Select("Parameters"));

            context.Settings.SymbolRenaming.RenameTypes = choices.Any(c => c.StartsWith("Types"));
            context.Settings.SymbolRenaming.RenameMethods = choices.Any(c => c.StartsWith("Methods"));
            context.Settings.SymbolRenaming.RenameProperties = choices.Any(c => c.StartsWith("Properties"));
            context.Settings.SymbolRenaming.RenameFields = choices.Any(c => c.StartsWith("Fields"));
            context.Settings.SymbolRenaming.RenameParameters = choices.Any(c => c.StartsWith("Parameters"));

            // Preserve public API
            AnsiConsole.WriteLine();
            var preservePublic = AnsiConsole.Confirm(
                "Preserve public API names?",
                defaultValue: context.Settings.SymbolRenaming.PreservePublicApi || context.IsPublicApi);

            context.Settings.SymbolRenaming.PreservePublicApi = preservePublic;

            if (preservePublic)
            {
                WriteInfo("Public types and members will keep their original names.");
            }
        }

        return Task.CompletedTask;
    }
}
