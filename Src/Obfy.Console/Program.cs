using System.CommandLine;
using System.Text.Json;
using Autofac;
using Logging.Core.DependencyInjection;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Spectre.Console;

namespace Obfy.Console;

public class Program
{
    private static IContainer? _container;

    public static async Task<int> Main(string[] args)
    {
        // Input argument
        var inputArgument = new Argument<FileInfo[]>(
            name: "input",
            description: "Input files to obfuscate (DLL, EXE, or .cs files)")
        {
            Arity = ArgumentArity.OneOrMore
        };

        // Options
        var outputOption = new Option<DirectoryInfo?>(
            aliases: ["--output", "-o"],
            description: "Output directory for obfuscated files");

        var configOption = new Option<FileInfo?>(
            aliases: ["--config", "-c"],
            description: "Path to JSON configuration file");

        var levelOption = new Option<string>(
            aliases: ["--level", "-l"],
            description: "Obfuscation level: minimal, standard, aggressive, or custom",
            getDefaultValue: () => "standard");

        var stringEncryptOption = new Option<bool>(
            name: "--string-encrypt",
            description: "Enable string encryption");

        var controlFlowOption = new Option<bool>(
            name: "--control-flow",
            description: "Enable control flow obfuscation");

        var renameOption = new Option<bool>(
            name: "--rename",
            description: "Enable symbol renaming");

        var antiDebugOption = new Option<bool>(
            name: "--anti-debug",
            description: "Enable anti-debugging protection");

        var stripMetadataOption = new Option<bool>(
            name: "--strip-metadata",
            description: "Remove debug metadata");

        var encryptResourcesOption = new Option<bool>(
            name: "--encrypt-resources",
            description: "Enable resource encryption");

        var preservePublicOption = new Option<bool>(
            name: "--preserve-public",
            description: "Preserve public API names");

        var mapOption = new Option<FileInfo?>(
            name: "--map",
            description: "Output symbol mapping to file");

        var reportOption = new Option<FileInfo?>(
            name: "--report",
            description: "Generate obfuscation report (HTML or JSON based on extension)");

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Analyze only, don't write output");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        var noLogoOption = new Option<bool>(
            name: "--no-logo",
            description: "Suppress the banner");

        // Root command
        var rootCommand = new RootCommand("Obfy - C# Obfuscation Tool")
        {
            inputArgument,
            outputOption,
            configOption,
            levelOption,
            stringEncryptOption,
            controlFlowOption,
            renameOption,
            antiDebugOption,
            stripMetadataOption,
            encryptResourcesOption,
            preservePublicOption,
            mapOption,
            reportOption,
            dryRunOption,
            verboseOption,
            noLogoOption
        };

        // Config generate command
        var configGenerateCommand = new Command("generate", "Generate a default configuration file");
        var configOutputOption = new Option<FileInfo>(
            aliases: ["--output", "-o"],
            description: "Output file path",
            getDefaultValue: () => new FileInfo("obfy.json"));
        var configLevelOption = new Option<string>(
            aliases: ["--level", "-l"],
            description: "Preset level for the configuration",
            getDefaultValue: () => "standard");

        configGenerateCommand.AddOption(configOutputOption);
        configGenerateCommand.AddOption(configLevelOption);

        configGenerateCommand.SetHandler(async (output, level) =>
        {
            await GenerateConfigAsync(output, level);
        }, configOutputOption, configLevelOption);

        var configCommand = new Command("config", "Configuration file operations")
        {
            configGenerateCommand
        };

        rootCommand.AddCommand(configCommand);

        // Main handler
        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArgument);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var config = context.ParseResult.GetValueForOption(configOption);
            var level = context.ParseResult.GetValueForOption(levelOption);
            var stringEncrypt = context.ParseResult.GetValueForOption(stringEncryptOption);
            var controlFlow = context.ParseResult.GetValueForOption(controlFlowOption);
            var rename = context.ParseResult.GetValueForOption(renameOption);
            var antiDebug = context.ParseResult.GetValueForOption(antiDebugOption);
            var stripMetadata = context.ParseResult.GetValueForOption(stripMetadataOption);
            var encryptResources = context.ParseResult.GetValueForOption(encryptResourcesOption);
            var preservePublic = context.ParseResult.GetValueForOption(preservePublicOption);
            var map = context.ParseResult.GetValueForOption(mapOption);
            var report = context.ParseResult.GetValueForOption(reportOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var noLogo = context.ParseResult.GetValueForOption(noLogoOption);

            if (!noLogo)
            {
                PrintBanner();
            }

            var settings = await BuildSettingsAsync(
                config, level, stringEncrypt, controlFlow, rename,
                antiDebug, stripMetadata, encryptResources, preservePublic);

            await RunObfuscationAsync(input, output, settings, map, report, dryRun, verbose);
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("Obfy")
            .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[dim]C# Obfuscation Tool[/]");
        AnsiConsole.WriteLine();
    }

    private static async Task<ObfySettings> BuildSettingsAsync(
        FileInfo? configFile,
        string level,
        bool stringEncrypt,
        bool controlFlow,
        bool rename,
        bool antiDebug,
        bool stripMetadata,
        bool encryptResources,
        bool preservePublic)
    {
        ObfySettings settings;

        if (configFile?.Exists == true)
        {
            var json = await File.ReadAllTextAsync(configFile.FullName);
            settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new ObfySettings();
        }
        else
        {
            var parsedLevel = ParseLevel(level);
            settings = ObfySettings.ForLevel(parsedLevel);
        }

        // Override with CLI options
        if (stringEncrypt) settings.StringEncryption.Enabled = true;
        if (controlFlow) settings.ControlFlow.Enabled = true;
        if (rename) settings.SymbolRenaming.Enabled = true;
        if (antiDebug) settings.Protection.AntiDebug = true;
        if (stripMetadata) settings.Metadata.RemoveDebugInfo = true;
        if (encryptResources) settings.ResourceEncryption.Enabled = true;
        if (preservePublic) settings.SymbolRenaming.PreservePublicApi = true;

        return settings;
    }

    private static ObfuscationLevel ParseLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "minimal" => ObfuscationLevel.Minimal,
            "standard" => ObfuscationLevel.Standard,
            "aggressive" => ObfuscationLevel.Aggressive,
            "custom" => ObfuscationLevel.Custom,
            _ => ObfuscationLevel.Standard
        };
    }

    private static async Task RunObfuscationAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        FileInfo? mapFile,
        FileInfo? reportFile,
        bool dryRun,
        bool verbose)
    {
        // Setup DI container
        var builder = new ContainerBuilder();
        builder.RegisterModule(new LoggingModule("Obfy.Console", enableConsoleOutput: verbose));
        builder.RegisterModule<ObfuscationModule>();
        _container = builder.Build();

        var service = _container.Resolve<IObfuscationService>();
        var reportService = _container.Resolve<IReportService>();

        var allSymbols = new Dictionary<string, string>();
        var successfulResults = new List<(ObfuscationResult Result, ObfySettings Settings)>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                foreach (var input in inputs)
                {
                    var task = ctx.AddTask($"[cyan]{input.Name}[/]");

                    if (!input.Exists)
                    {
                        AnsiConsole.MarkupLine($"[red]File not found: {input.FullName}[/]");
                        task.Increment(100);
                        continue;
                    }

                    var outputPath = output != null
                        ? Path.Combine(output.FullName, input.Name)
                        : null;

                    if (dryRun)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Dry run:[/] Would obfuscate {input.Name}");
                        task.Increment(100);
                        continue;
                    }

                    var result = await service.ObfuscateAsync(
                        input.FullName,
                        outputPath,
                        settings);

                    task.Increment(100);

                    if (result.Success)
                    {
                        DisplaySuccess(input.Name, result);
                        successfulResults.Add((result, settings));

                        // Collect symbols for map
                        foreach (var (key, value) in result.SymbolMap)
                        {
                            allSymbols[key] = value;
                        }
                    }
                    else
                    {
                        DisplayError(input.Name, result);
                    }
                }
            });

        // Write symbol map if requested
        if (mapFile != null && allSymbols.Count > 0)
        {
            await service.WriteSymbolMapAsync(allSymbols, mapFile.FullName);
            AnsiConsole.MarkupLine($"[green]Symbol map written to {mapFile.FullName}[/]");
        }

        // Generate report if requested
        if (reportFile != null && successfulResults.Count > 0)
        {
            var format = Path.GetExtension(reportFile.FullName).ToLowerInvariant() == ".json"
                ? ReportFormat.Json
                : ReportFormat.Html;

            // For single file, generate report directly
            // For multiple files, use the first result (or could aggregate in future)
            var (result, usedSettings) = successfulResults[0];
            var report = reportService.BuildReport(result, usedSettings);
            await reportService.GenerateReportAsync(report, reportFile.FullName, format);
            AnsiConsole.MarkupLine($"[green]Report written to {reportFile.FullName}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Obfuscation complete![/]");
    }

    private static void DisplaySuccess(string fileName, ObfuscationResult result)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Status", "[green]Success[/]");
        table.AddRow("File", fileName);
        table.AddRow("Output", result.OutputPath ?? "N/A");
        table.AddRow("Duration", $"{result.ElapsedTime.TotalMilliseconds:F0}ms");

        if (result.Statistics.TotalTransformations > 0)
        {
            table.AddRow("Strings Encrypted", result.Statistics.StringsEncrypted.ToString());
            table.AddRow("Resources Encrypted", result.Statistics.ResourcesEncrypted.ToString());
            table.AddRow("Types Renamed", result.Statistics.TypesRenamed.ToString());
            table.AddRow("Methods Renamed", result.Statistics.MethodsRenamed.ToString());
            table.AddRow("Control Flow", result.Statistics.MethodsControlFlowObfuscated.ToString());
            table.AddRow("Protections", result.Statistics.ProtectionsApplied.ToString());
            table.AddRow("Metadata Removed", result.Statistics.MetadataItemsRemoved.ToString());
            table.AddRow("[bold]Total Transformations[/]", $"[bold]{result.Statistics.TotalTransformations}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static void DisplayError(string fileName, ObfuscationResult result)
    {
        AnsiConsole.MarkupLine($"[red]Failed to obfuscate {fileName}[/]");
        AnsiConsole.MarkupLine($"[red]Error: {result.ErrorMessage}[/]");

        if (result.Exception != null)
        {
            AnsiConsole.WriteException(result.Exception);
        }
    }

    private static async Task GenerateConfigAsync(FileInfo output, string level)
    {
        var parsedLevel = ParseLevel(level);
        var settings = ObfySettings.ForLevel(parsedLevel);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(settings, options);
        await File.WriteAllTextAsync(output.FullName, json);

        AnsiConsole.MarkupLine($"[green]Configuration file created: {output.FullName}[/]");
    }
}
