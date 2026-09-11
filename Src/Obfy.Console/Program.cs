using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autofac;
using Logging.Core.DependencyInjection;
using Obfy.Console.Wizard;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Spectre.Console;

namespace Obfy.Console;

public class Program
{
    private static IContainer? _container;

    // Expose options and arguments as internal static for testing
    internal static Argument<FileInfo[]> InputArgument { get; private set; } = null!;
    internal static Option<DirectoryInfo?> OutputOption { get; private set; } = null!;
    internal static Option<FileInfo?> ConfigOption { get; private set; } = null!;
    internal static Option<string> LevelOption { get; private set; } = null!;
    internal static Option<bool> StringEncryptOption { get; private set; } = null!;
    internal static Option<bool> ControlFlowOption { get; private set; } = null!;
    internal static Option<bool> RenameOption { get; private set; } = null!;
    internal static Option<bool> AntiDebugOption { get; private set; } = null!;
    internal static Option<bool> AntiTamperOption { get; private set; } = null!;
    internal static Option<bool> AntiDecompilerOption { get; private set; } = null!;
    internal static Option<bool> AntiDumpOption { get; private set; } = null!;
    internal static Option<bool> ReferenceProxyOption { get; private set; } = null!;
    internal static Option<bool> NoStringEncryptOption { get; private set; } = null!;
    internal static Option<bool> NoRenameOption { get; private set; } = null!;
    internal static Option<bool> NoControlFlowOption { get; private set; } = null!;
    internal static Option<bool> StripMetadataOption { get; private set; } = null!;
    internal static Option<bool> EncryptResourcesOption { get; private set; } = null!;
    internal static Option<bool> EncryptConstantsOption { get; private set; } = null!;
    internal static Option<bool> PreservePublicOption { get; private set; } = null!;
    internal static Option<FileInfo?> MapOption { get; private set; } = null!;
    internal static Option<FileInfo?> ReportOption { get; private set; } = null!;
    internal static Option<bool> DryRunOption { get; private set; } = null!;
    internal static Option<bool> VerboseOption { get; private set; } = null!;
    internal static Option<bool> NoLogoOption { get; private set; } = null!;
    internal static Option<bool> MergeOption { get; private set; } = null!;
    internal static Option<bool> InternalizeOption { get; private set; } = null!;

    /// <summary>
    /// Creates the root command with all options and subcommands.
    /// Exposed for testing purposes.
    /// </summary>
    internal static RootCommand CreateRootCommand()
    {
        // Input argument
        InputArgument = new Argument<FileInfo[]>(
            name: "input",
            description: "Input files to obfuscate (DLL, EXE, or .cs files)")
        {
            Arity = ArgumentArity.OneOrMore
        };

        // Options
        OutputOption = new Option<DirectoryInfo?>(
            aliases: ["--output", "-o"],
            description: "Output directory for obfuscated files");

        ConfigOption = new Option<FileInfo?>(
            aliases: ["--config", "-c"],
            description: "Path to JSON configuration file");

        LevelOption = new Option<string>(
            aliases: ["--level", "-l"],
            description: "Obfuscation level: minimal, standard, aggressive, or custom",
            getDefaultValue: () => "standard");

        StringEncryptOption = new Option<bool>(
            name: "--string-encrypt",
            description: "Enable string encryption");

        ControlFlowOption = new Option<bool>(
            name: "--control-flow",
            description: "Enable control flow obfuscation");

        RenameOption = new Option<bool>(
            name: "--rename",
            description: "Enable symbol renaming");

        AntiDebugOption = new Option<bool>(
            name: "--anti-debug",
            description: "Enable anti-debugging protection");

        AntiTamperOption = new Option<bool>(
            name: "--anti-tamper",
            description: "Enable anti-tamper protection");

        AntiDecompilerOption = new Option<bool>(
            name: "--anti-decompiler",
            description: "Enable anti-decompiler protection");

        AntiDumpOption = new Option<bool>(
            name: "--anti-dump",
            description: "Enable anti-dump protection");

        ReferenceProxyOption = new Option<bool>(
            name: "--reference-proxy",
            description: "Enable reference proxy");

        NoStringEncryptOption = new Option<bool>(
            name: "--no-string-encryption",
            description: "Disable string encryption");

        NoRenameOption = new Option<bool>(
            name: "--no-symbol-renaming",
            description: "Disable symbol renaming");

        NoControlFlowOption = new Option<bool>(
            name: "--no-control-flow",
            description: "Disable control flow obfuscation");

        StripMetadataOption = new Option<bool>(
            name: "--strip-metadata",
            description: "Remove debug metadata");

        EncryptResourcesOption = new Option<bool>(
            name: "--encrypt-resources",
            description: "Enable resource encryption");

        EncryptConstantsOption = new Option<bool>(
            name: "--encrypt-constants",
            description: "Enable constant encryption");

        PreservePublicOption = new Option<bool>(
            name: "--preserve-public",
            description: "Preserve public API names");

        MapOption = new Option<FileInfo?>(
            name: "--map",
            description: "Output symbol mapping to file");

        ReportOption = new Option<FileInfo?>(
            name: "--report",
            description: "Generate obfuscation report (HTML or JSON based on extension)");

        DryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Analyze only, don't write output");

        VerboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        NoLogoOption = new Option<bool>(
            name: "--no-logo",
            description: "Suppress the banner");

        MergeOption = new Option<bool>(
            name: "--merge",
            description: "Merge all input assemblies into one before obfuscating");

        InternalizeOption = new Option<bool>(
            name: "--internalize",
            description: "Make merged types internal (improves obfuscation)",
            getDefaultValue: () => true);

        // Root command
        var rootCommand = new RootCommand("Obfy - C# Obfuscation Tool")
        {
            InputArgument,
            OutputOption,
            ConfigOption,
            LevelOption,
            StringEncryptOption,
            ControlFlowOption,
            RenameOption,
            AntiDebugOption,
            AntiTamperOption,
            AntiDecompilerOption,
            AntiDumpOption,
            ReferenceProxyOption,
            NoStringEncryptOption,
            NoRenameOption,
            NoControlFlowOption,
            StripMetadataOption,
            EncryptResourcesOption,
            EncryptConstantsOption,
            PreservePublicOption,
            MapOption,
            ReportOption,
            DryRunOption,
            VerboseOption,
            NoLogoOption,
            MergeOption,
            InternalizeOption
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
            await GenerateConfigAsync(output, level).ConfigureAwait(false);
        }, configOutputOption, configLevelOption);

        // Config wizard command
        var wizardCommand = new Command("wizard", "Interactive wizard to create a configuration file");
        var wizardOutputOption = new Option<FileInfo>(
            aliases: ["--output", "-o"],
            description: "Output file path",
            getDefaultValue: () => new FileInfo("obfy.json"));
        var quickModeOption = new Option<bool>(
            aliases: ["--quick", "-q"],
            description: "Quick mode - just select a preset level");

        wizardCommand.AddOption(wizardOutputOption);
        wizardCommand.AddOption(quickModeOption);

        wizardCommand.SetHandler(async (output, quick) =>
        {
            var wizard = new ConfigurationWizard();
            await wizard.RunAsync(output, quick).ConfigureAwait(false);
        }, wizardOutputOption, quickModeOption);

        var configCommand = new Command("config", "Configuration file operations")
        {
            configGenerateCommand,
            wizardCommand
        };

        rootCommand.AddCommand(configCommand);

        // Set a default handler for the root command
        // This enables parsing to recognize the root command as valid
        rootCommand.SetHandler(() => { });

        return rootCommand;
    }

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CreateRootCommand();

        // Main handler
        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(InputArgument);
            var output = context.ParseResult.GetValueForOption(OutputOption);
            var config = context.ParseResult.GetValueForOption(ConfigOption);
            var level = context.ParseResult.GetValueForOption(LevelOption);
            var stringEncrypt = context.ParseResult.GetValueForOption(StringEncryptOption);
            var controlFlow = context.ParseResult.GetValueForOption(ControlFlowOption);
            var rename = context.ParseResult.GetValueForOption(RenameOption);
            var antiDebug = context.ParseResult.GetValueForOption(AntiDebugOption);
            var antiTamper = context.ParseResult.GetValueForOption(AntiTamperOption);
            var antiDecompiler = context.ParseResult.GetValueForOption(AntiDecompilerOption);
            var antiDump = context.ParseResult.GetValueForOption(AntiDumpOption);
            var referenceProxy = context.ParseResult.GetValueForOption(ReferenceProxyOption);
            var noStringEncrypt = context.ParseResult.GetValueForOption(NoStringEncryptOption);
            var noRename = context.ParseResult.GetValueForOption(NoRenameOption);
            var noControlFlow = context.ParseResult.GetValueForOption(NoControlFlowOption);
            var stripMetadata = context.ParseResult.GetValueForOption(StripMetadataOption);
            var encryptResources = context.ParseResult.GetValueForOption(EncryptResourcesOption);
            var encryptConstants = context.ParseResult.GetValueForOption(EncryptConstantsOption);
            var preservePublic = context.ParseResult.GetValueForOption(PreservePublicOption);
            var map = context.ParseResult.GetValueForOption(MapOption);
            var report = context.ParseResult.GetValueForOption(ReportOption);
            var dryRun = context.ParseResult.GetValueForOption(DryRunOption);
            var verbose = context.ParseResult.GetValueForOption(VerboseOption);
            var noLogo = context.ParseResult.GetValueForOption(NoLogoOption);
            var merge = context.ParseResult.GetValueForOption(MergeOption);
            var internalize = context.ParseResult.GetValueForOption(InternalizeOption);

            if (!noLogo)
            {
                PrintBanner();
            }

            try
            {
                var settings = await BuildSettingsAsync(
                    config, level ?? "standard", stringEncrypt, controlFlow, rename,
                    antiDebug, stripMetadata, encryptResources, preservePublic,
                    antiTamper, antiDecompiler, noStringEncrypt, noRename,
                    antiDump, referenceProxy, encryptConstants, noControlFlow).ConfigureAwait(false);

                if (merge)
                {
                    settings.AssemblyMerge.Enabled = true;
                    settings.AssemblyMerge.Internalize = internalize;
                }

                context.ExitCode = await RunObfuscationAsync(input, output, settings, map, report, dryRun, verbose, merge).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or InvalidOperationException or JsonException or IOException)
            {
                AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
                context.ExitCode = 1;
            }
        });

        return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
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
        bool preservePublic,
        bool antiTamper = false,
        bool antiDecompiler = false,
        bool noStringEncrypt = false,
        bool noRename = false,
        bool antiDump = false,
        bool referenceProxy = false,
        bool encryptConstants = false,
        bool noControlFlow = false)
    {
        ObfySettings settings;

        if (configFile != null)
        {
            if (!configFile.Exists)
                throw new FileNotFoundException($"Config file not found: {configFile.FullName}");

            var json = await File.ReadAllTextAsync(configFile.FullName).ConfigureAwait(false);
            settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? throw new InvalidOperationException("Config file deserialized to null.");

            // The per-technique flags in a config file are authoritative. Treat file-sourced settings
            // as Custom so ObfuscationService does not re-run ApplyLevel() and overwrite them
            // (a generated config already contains the fully resolved flags for its level).
            settings.Level = ObfuscationLevel.Custom;
        }
        else
        {
            settings = ObfySettings.ForLevel(ParseLevel(level));
        }

        var anyOverride = stringEncrypt || controlFlow || rename || antiDebug || stripMetadata
            || encryptResources || preservePublic || antiTamper || antiDecompiler
            || noStringEncrypt || noRename
            || antiDump || referenceProxy || encryptConstants || noControlFlow;

        if (stringEncrypt) settings.StringEncryption.Enabled = true;
        if (noStringEncrypt) settings.StringEncryption.Enabled = false;
        if (controlFlow) settings.ControlFlow.Enabled = true;
        if (noControlFlow) settings.ControlFlow.Enabled = false;
        if (rename) settings.SymbolRenaming.Enabled = true;
        if (noRename) settings.SymbolRenaming.Enabled = false;
        if (antiDebug) settings.Protection.AntiDebug = true;
        if (antiTamper) settings.Protection.AntiTamper.Enabled = true;
        if (antiDecompiler) settings.Protection.AntiDecompiler.Enabled = true;
        if (antiDump) settings.Protection.AntiDump = true;
        if (referenceProxy) settings.Protection.ReferenceProxy = true;
        if (stripMetadata) settings.Metadata.RemoveDebugInfo = true;
        if (encryptResources) settings.ResourceEncryption.Enabled = true;
        if (encryptConstants) settings.ConstantEncryption.Enabled = true;
        if (preservePublic) settings.SymbolRenaming.PreservePublicApi = true;

        if (anyOverride)
            settings.Level = ObfuscationLevel.Custom;

        return settings;
    }

    private static ObfuscationLevel ParseLevel(string level)
    {
        if (!Enum.TryParse<ObfuscationLevel>(level, ignoreCase: true, out var parsed))
        {
            throw new ArgumentException($"Unknown obfuscation level '{level}'. Valid values: minimal, standard, aggressive, custom.");
        }

        return parsed;
    }

    private static async Task<int> RunObfuscationAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        FileInfo? mapFile,
        FileInfo? reportFile,
        bool dryRun,
        bool verbose,
        bool merge = false)
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
        var anyFailed = false;

        if (merge && inputs.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Merge requires at least two input assemblies.[/]");
            return 1;
        }

        if (merge)
        {
            anyFailed = await RunMergeObfuscationAsync(inputs, output, settings, allSymbols, successfulResults, service, dryRun).ConfigureAwait(false);
        }
        else
        {
            anyFailed = await RunStandardObfuscationAsync(inputs, output, settings, allSymbols, successfulResults, service, dryRun).ConfigureAwait(false);
        }

        // Write symbol map if requested
        if (mapFile != null && allSymbols.Count > 0)
        {
            await service.WriteSymbolMapAsync(allSymbols, mapFile.FullName).ConfigureAwait(false);
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
            await reportService.GenerateReportAsync(report, reportFile.FullName, format).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Report written to {reportFile.FullName}[/]");
        }

        AnsiConsole.WriteLine();
        if (anyFailed)
        {
            AnsiConsole.MarkupLine("[red]Obfuscation finished with errors.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Obfuscation complete![/]");
        return 0;
    }

    private static async Task<bool> RunMergeObfuscationAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        Dictionary<string, string> allSymbols,
        List<(ObfuscationResult Result, ObfySettings Settings)> successfulResults,
        IObfuscationService service,
        bool dryRun)
    {
        var primaryInput = inputs[0];
        var outputPath = output != null
            ? Path.Combine(output.FullName, primaryInput.Name)
            : Path.Combine(
                Path.GetDirectoryName(primaryInput.FullName) ?? ".",
                Path.GetFileNameWithoutExtension(primaryInput.Name) + ".merged" + Path.GetExtension(primaryInput.Name));

        var inputNames = string.Join(", ", inputs.Select(i => i.Name));
        AnsiConsole.MarkupLine($"[cyan]Merging assemblies:[/] {inputNames}");

        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Dry run:[/] Would merge and obfuscate {inputs.Length} assemblies");
            return false;
        }

        var failed = false;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Merging and obfuscating...", async ctx =>
            {
                var inputPaths = inputs.Select(i => i.FullName).ToArray();

                var result = await service.MergeAndObfuscateAsync(
                    inputPaths,
                    outputPath,
                    settings).ConfigureAwait(false);

                if (result.Success)
                {
                    DisplaySuccess($"Merged ({inputs.Length} assemblies)", result);
                    successfulResults.Add((result, settings));

                    foreach (var (key, value) in result.SymbolMap)
                    {
                        allSymbols[key] = value;
                    }
                }
                else
                {
                    DisplayError("Merge", result);
                    failed = true;
                }
            }).ConfigureAwait(false);
        return failed;
    }

    private static async Task<bool> RunStandardObfuscationAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        Dictionary<string, string> allSymbols,
        List<(ObfuscationResult Result, ObfySettings Settings)> successfulResults,
        IObfuscationService service,
        bool dryRun)
    {
        var failed = false;
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
                        failed = true;
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
                        settings).ConfigureAwait(false);

                    task.Increment(100);

                    if (result.Success)
                    {
                        DisplaySuccess(input.Name, result);
                        successfulResults.Add((result, settings));

                        foreach (var (key, value) in result.SymbolMap)
                        {
                            allSymbols[key] = value;
                        }
                    }
                    else
                    {
                        DisplayError(input.Name, result);
                        failed = true;
                    }
                }
            }).ConfigureAwait(false);
        return failed;
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

        // Surface skipped items and warnings in normal output (not only in --report) so partial
        // protection or a protection that cannot take effect is never silently reported as success.
        if (result.SkippedItems.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {result.SkippedItems.Count} item(s) were skipped and left unobfuscated:[/]");
            foreach (var group in result.SkippedItems
                         .GroupBy(s => s.Details ?? s.Reason.ToString())
                         .OrderByDescending(g => g.Count()))
            {
                AnsiConsole.MarkupLine($"  [yellow]- {Markup.Escape(group.Key)}: {group.Count()}[/]");
            }
        }

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(warning)}[/]");
        }
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
        var node = JsonNode.Parse(json)!.AsObject();
        node.Insert(0, "$schema", "https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json");
        await File.WriteAllTextAsync(output.FullName, node.ToJsonString(options)).ConfigureAwait(false);

        AnsiConsole.MarkupLine($"[green]Configuration file created: {output.FullName}[/]");
    }
}
