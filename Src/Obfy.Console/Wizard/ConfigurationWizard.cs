using System.Text.Json;
using Obfy.Console.Wizard.Steps;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Spectre.Console;

namespace Obfy.Console.Wizard;

/// <summary>
/// Interactive configuration wizard orchestrator.
/// </summary>
public class ConfigurationWizard
{
    private readonly List<WizardStep> _quickModeSteps;
    private readonly List<WizardStep> _advancedModeSteps;

    public ConfigurationWizard()
    {
        _quickModeSteps =
        [
            new UseCaseStep(),
            new ProtectionLevelStep(),
            new OutputStep()
        ];

        _advancedModeSteps =
        [
            new ModeSelectionStep(),
            new UseCaseStep(),
            new ProtectionLevelStep(),
            new StringEncryptionStep(),
            new ControlFlowStep(),
            new SymbolRenamingStep(),
            new ProtectionStep(),
            new ExclusionStep(),
            new OutputStep()
        ];
    }

    /// <summary>
    /// Run the wizard.
    /// </summary>
    public async Task<int> RunAsync(FileInfo output, bool quickMode)
    {
        var context = new WizardContext
        {
            OutputFile = output,
            IsQuickMode = quickMode
        };

        // Show welcome banner
        ShowWelcome();

        // Get the steps based on mode
        List<WizardStep> steps;
        if (quickMode)
        {
            steps = _quickModeSteps;
        }
        else
        {
            // First step determines if user wants quick or advanced
            await _advancedModeSteps[0].ExecuteAsync(context).ConfigureAwait(false);
            if (context.Cancelled)
            {
                return 1;
            }

            steps = context.IsQuickMode ? _quickModeSteps : _advancedModeSteps.Skip(1).ToList();
        }

        // Execute steps
        foreach (var step in steps)
        {
            await step.ExecuteAsync(context).ConfigureAwait(false);

            if (context.Cancelled)
            {
                AnsiConsole.MarkupLine("[yellow]Wizard cancelled.[/]");
                return 1;
            }
        }

        return 0;
    }

    private static void ShowWelcome()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("Obfy")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[dim]Configuration Wizard[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Generate the configuration JSON.
    /// </summary>
    public static async Task<bool> GenerateConfigAsync(WizardContext context)
    {
        // Apply use case defaults
        ApplyUseCaseDefaults(context);

        // Check for existing file
        if (context.OutputFile.Exists)
        {
            var overwrite = AnsiConsole.Confirm(
                $"[yellow]File '{context.OutputFile.Name}' already exists. Overwrite?[/]",
                defaultValue: false);

            if (!overwrite)
            {
                var newPath = AnsiConsole.Ask<string>("Enter new file path:", "obfy.json");
                context.OutputFile = new FileInfo(newPath);
            }
        }

        // Serialize settings
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(context.Settings, options);

        try
        {
            // Ensure directory exists
            context.OutputFile.Directory?.Create();
            await File.WriteAllTextAsync(context.OutputFile.FullName, json).ConfigureAwait(false);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]Configuration saved to:[/] {context.OutputFile.FullName}");
            AnsiConsole.WriteLine();

            // Show next steps
            AnsiConsole.MarkupLine("[dim]Next steps:[/]");
            AnsiConsole.MarkupLine($"  [cyan]obfy input.dll -c {context.OutputFile.Name}[/]");
            AnsiConsole.WriteLine();

            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to write configuration: {ex.Message}[/]");
            return false;
        }
    }

    /// <summary>
    /// Display a summary of the configuration.
    /// </summary>
    public static void DisplaySummary(WizardContext context)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan]Configuration Summary[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Setting")
            .AddColumn("Value");

        var s = context.Settings;

        table.AddRow("Level", s.Level.ToString());
        table.AddRow("String Encryption", FormatBool(s.StringEncryption.Enabled));
        table.AddRow("Control Flow", s.ControlFlow.Enabled ? $"Yes ({s.ControlFlow.Intensity}%)" : "No");
        table.AddRow("Symbol Renaming", FormatBool(s.SymbolRenaming.Enabled));
        table.AddRow("Anti-Debug", FormatBool(s.Protection.AntiDebug));
        table.AddRow("Anti-Tamper", FormatBool(s.Protection.AntiTamper.Enabled));
        table.AddRow("Anti-Decompiler", FormatBool(s.Protection.AntiDecompiler.Enabled));
        table.AddRow("Resource Encryption", FormatBool(s.ResourceEncryption.Enabled));
        table.AddRow("Constant Encryption", FormatBool(s.ConstantEncryption.Enabled));
        table.AddRow("Preserve Public API", FormatBool(s.SymbolRenaming.PreservePublicApi));
        table.AddRow("Preserve XAML", FormatBool(s.SymbolRenaming.PreserveXaml));
        table.AddRow("Runtime Profile", s.RuntimeProfile.ToString());
        if (s.Signing.Enabled)
            table.AddRow("Signing", s.Signing.KeyFile ?? "(enabled, no key file)");

        if (s.Exclusions.Namespaces.Count > 0)
        {
            table.AddRow("Excluded Namespaces", string.Join(", ", s.Exclusions.Namespaces));
        }

        AnsiConsole.Write(table);
    }

    private static string FormatBool(bool value) => value ? "[green]Yes[/]" : "[dim]No[/]";

    internal static void ApplyUseCaseDefaults(WizardContext context)
    {
        switch (context.UseCase)
        {
            case "Class Library / NuGet Package":
                context.Settings.SymbolRenaming.PreservePublicApi = true;
                break;

            case "Desktop Application":
                context.Settings.SymbolRenaming.PreserveXaml = true;
                break;

            case "Blazor WebAssembly":
                context.Settings.RuntimeProfile = RuntimeProfile.BlazorWasm;
                break;

            case "MAUI / Mobile":
                context.Settings.SymbolRenaming.PreserveXaml = true;
                break;

            case "Game (Unity)":
                foreach (var ns in PlatformExclusions.UnityNamespaces)
                    context.Settings.Exclusions.Namespaces.Add(ns);
                context.Settings.RuntimeProfile = RuntimeProfile.UnityIl2Cpp;
                break;

            case "Web Application (ASP.NET)":
                foreach (var attribute in PlatformExclusions.AspNetMvcAttributes)
                    context.Settings.Exclusions.Attributes.Add(attribute);
                break;
        }

        // If marked as public API, always preserve public names
        if (context.IsPublicApi)
        {
            context.Settings.SymbolRenaming.PreservePublicApi = true;
        }
    }
}
