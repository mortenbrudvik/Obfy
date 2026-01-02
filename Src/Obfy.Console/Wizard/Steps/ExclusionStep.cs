using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to configure exclusion rules.
/// </summary>
public class ExclusionStep : WizardStep
{
    public override string Title => "Exclusions";

    public override Task ExecuteAsync(WizardContext context)
    {
        // Skip in quick mode
        if (context.IsQuickMode)
        {
            return Task.CompletedTask;
        }

        WriteHeader(Title);

        WriteHint("Exclusions prevent specific code from being obfuscated.");
        WriteHint("Use wildcards like 'MyApp.Public.*' to match patterns.");
        AnsiConsole.WriteLine();

        var addExclusions = AnsiConsole.Confirm(
            "Would you like to add exclusion rules?",
            defaultValue: false);

        if (!addExclusions)
        {
            return Task.CompletedTask;
        }

        // Namespace exclusions
        AnsiConsole.WriteLine();
        WriteHint("Enter namespace patterns to exclude (comma-separated, or leave empty):");
        var namespaces = AnsiConsole.Ask<string>("Namespaces:", "");

        if (!string.IsNullOrWhiteSpace(namespaces))
        {
            foreach (var ns in namespaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                context.Settings.Exclusions.Namespaces.Add(ns);
            }
        }

        // Type exclusions
        AnsiConsole.WriteLine();
        WriteHint("Enter type patterns to exclude (comma-separated, or leave empty):");
        var types = AnsiConsole.Ask<string>("Types:", "");

        if (!string.IsNullOrWhiteSpace(types))
        {
            foreach (var type in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                context.Settings.Exclusions.Types.Add(type);
            }
        }

        // Method exclusions
        AnsiConsole.WriteLine();
        WriteHint("Enter method patterns to exclude (comma-separated, or leave empty):");
        var methods = AnsiConsole.Ask<string>("Methods:", "");

        if (!string.IsNullOrWhiteSpace(methods))
        {
            foreach (var method in methods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                context.Settings.Exclusions.Methods.Add(method);
            }
        }

        // Summary
        var totalExclusions = context.Settings.Exclusions.Namespaces.Count +
                              context.Settings.Exclusions.Types.Count +
                              context.Settings.Exclusions.Methods.Count;

        if (totalExclusions > 0)
        {
            AnsiConsole.WriteLine();
            WriteInfo($"Added {totalExclusions} exclusion rule(s).");
        }

        return Task.CompletedTask;
    }
}
