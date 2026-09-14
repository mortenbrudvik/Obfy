using System.CommandLine;
using System.Text.Json;
using System.Xml;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Autofac;
using Logging.Core.Configuration;
using Logging.Core.DependencyInjection;
using Obfy.Console.Wizard;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.Core.Services.Solution;
using Spectre.Console;

namespace Obfy.Console;

public class Program
{
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
    internal static Option<bool> ProxyExternalOption { get; private set; } = null!;
    internal static Option<bool> EncryptMethodsOption { get; private set; } = null!;
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
    internal static Option<string?> WatermarkIdOption { get; private set; } = null!;
    internal static Option<bool> VirtualizeOption { get; private set; } = null!;
    internal static Option<bool> IncrementalOption { get; private set; } = null!;

    /// <summary>
    /// Creates the root command with all options and subcommands.
    /// Exposed for testing purposes.
    /// </summary>
    internal static RootCommand CreateRootCommand()
    {
        // Input argument
        InputArgument = new Argument<FileInfo[]>("input")
        {
            Description = "Input files to obfuscate (DLL, EXE, .cs, .sln, .slnx, or project files)",
            Arity = ArgumentArity.OneOrMore
        };

        // Options
        OutputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Output directory for obfuscated files"
        };

        ConfigOption = new Option<FileInfo?>("--config", "-c")
        {
            Description = "Path to JSON configuration file"
        };

        LevelOption = new Option<string>("--level", "-l")
        {
            Description = "Obfuscation level: minimal, standard, aggressive, or custom",
            DefaultValueFactory = _ => "standard"
        };

        StringEncryptOption = new Option<bool>("--string-encrypt")
        {
            Description = "Enable string encryption"
        };

        ControlFlowOption = new Option<bool>("--control-flow")
        {
            Description = "Enable control flow obfuscation"
        };

        RenameOption = new Option<bool>("--rename")
        {
            Description = "Enable symbol renaming"
        };

        AntiDebugOption = new Option<bool>("--anti-debug")
        {
            Description = "Enable anti-debugging protection"
        };

        AntiTamperOption = new Option<bool>("--anti-tamper")
        {
            Description = "Enable anti-tamper protection"
        };

        AntiDecompilerOption = new Option<bool>("--anti-decompiler")
        {
            Description = "Enable anti-decompiler protection"
        };

        AntiDumpOption = new Option<bool>("--anti-dump")
        {
            Description = "Enable anti-dump protection"
        };

        ReferenceProxyOption = new Option<bool>("--reference-proxy")
        {
            Description = "Enable reference proxy"
        };

        ProxyExternalOption = new Option<bool>("--proxy-external")
        {
            Description = "Also proxy selected out-of-module calls (enables --reference-proxy). Skips compiler/interop/pointer signatures."
        };

        EncryptMethodsOption = new Option<bool>("--encrypt-methods")
        {
            Description = "Encrypt method IL in the PE image"
        };

        NoStringEncryptOption = new Option<bool>("--no-string-encryption")
        {
            Description = "Disable string encryption"
        };

        NoRenameOption = new Option<bool>("--no-symbol-renaming")
        {
            Description = "Disable symbol renaming"
        };

        NoControlFlowOption = new Option<bool>("--no-control-flow")
        {
            Description = "Disable control flow obfuscation"
        };

        StripMetadataOption = new Option<bool>("--strip-metadata")
        {
            Description = "Remove debug metadata"
        };

        EncryptResourcesOption = new Option<bool>("--encrypt-resources")
        {
            Description = "Enable resource encryption"
        };

        EncryptConstantsOption = new Option<bool>("--encrypt-constants")
        {
            Description = "Enable constant encryption"
        };

        PreservePublicOption = new Option<bool>("--preserve-public")
        {
            Description = "Preserve public API names"
        };

        MapOption = new Option<FileInfo?>("--map")
        {
            Description = "Output symbol mapping to file"
        };

        ReportOption = new Option<FileInfo?>("--report")
        {
            Description = "Generate obfuscation report (HTML or JSON based on extension)"
        };

        DryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Analyze only, don't write output"
        };

        VerboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        NoLogoOption = new Option<bool>("--no-logo")
        {
            Description = "Suppress the banner"
        };

        MergeOption = new Option<bool>("--merge")
        {
            Description = "Merge all input assemblies into one before obfuscating"
        };

        InternalizeOption = new Option<bool>("--internalize")
        {
            Description = "Make merged types internal (improves obfuscation)",
            DefaultValueFactory = _ => true
        };

        WatermarkIdOption = new Option<string?>("--watermark-id")
        {
            Description = "Enable watermarking and set watermark.id (trimmed; whitespace-only is an error)"
        };

        VirtualizeOption = new Option<bool>("--virtualize")
        {
            Description = "Enable IL virtualization of eligible methods (no EH/generics/byref/custom structs/ctors; gated off NativeAOT / IL2CPP / Blazor WASM)"
        };

        IncrementalOption = new Option<bool>("--incremental")
        {
            Description = "Skip re-obfuscation when input and settings are unchanged"
        };

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
            ProxyExternalOption,
            EncryptMethodsOption,
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
            InternalizeOption,
            WatermarkIdOption,
            VirtualizeOption,
            IncrementalOption
        };

        // Config generate command
        var configGenerateCommand = new Command("generate", "Generate a default configuration file");
        var configOutputOption = new Option<FileInfo>("--output", "-o")
        {
            Description = "Output file path",
            DefaultValueFactory = _ => new FileInfo("obfy.json")
        };
        var configLevelOption = new Option<string>("--level", "-l")
        {
            Description = "Preset level for the configuration",
            DefaultValueFactory = _ => "standard"
        };

        configGenerateCommand.Options.Add(configOutputOption);
        configGenerateCommand.Options.Add(configLevelOption);

        configGenerateCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.GetValue(configOutputOption);
            var level = parseResult.GetValue(configLevelOption);
            await GenerateConfigAsync(output!, level!).ConfigureAwait(false);
        });

        // Config wizard command
        var wizardCommand = new Command("wizard", "Interactive wizard to create a configuration file");
        var wizardOutputOption = new Option<FileInfo>("--output", "-o")
        {
            Description = "Output file path",
            DefaultValueFactory = _ => new FileInfo("obfy.json")
        };
        var quickModeOption = new Option<bool>("--quick", "-q")
        {
            Description = "Quick mode - just select a preset level"
        };

        wizardCommand.Options.Add(wizardOutputOption);
        wizardCommand.Options.Add(quickModeOption);

        wizardCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.GetValue(wizardOutputOption);
            var quick = parseResult.GetValue(quickModeOption);
            var wizard = new ConfigurationWizard();
            await wizard.RunAsync(output!, quick).ConfigureAwait(false);
        });

        var configCommand = new Command("config", "Configuration file operations")
        {
            configGenerateCommand,
            wizardCommand
        };

        rootCommand.Subcommands.Add(configCommand);

        rootCommand.SetAction(HandleRootCommandAsync);

        return rootCommand;
    }

    public static async Task<int> Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var text = e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject?.ToString();
            System.Console.Error.WriteLine($"Unhandled exception: {text}");
            LoggingConfiguration.Shutdown();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Console.Error.WriteLine($"Unobserved task exception: {e.Exception}");
            LoggingConfiguration.Flush();
            Environment.ExitCode = 1;
            e.SetObserved();
        };

        var rootCommand = CreateRootCommand();
        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    internal static bool IsSolutionOrProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the CLI should run a closed-set on loose files: at least two assembly
    /// paths exist on disk, and the argument list contains no solution or project files.
    /// Does not inspect <c>--merge</c>; the caller must take the merge path first.
    /// Source files and other non-assemblies are skipped later; they do not disable closed-set.
    /// Unlike a solution session, this does not analyze projects or apply csproj hints.
    /// </summary>
    internal static bool ShouldUseLooseClosedSet(IReadOnlyList<FileInfo> inputs)
    {
        if (inputs.Count < 2)
            return false;

        foreach (var input in inputs)
        {
            if (IsSolutionOrProject(input.FullName))
                return false;
        }

        return inputs.Count(static i => IsAssemblyFile(i.FullName) && File.Exists(i.FullName)) >= 2;
    }

    private static bool IsSolutionFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssemblyFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceFile(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> HandleRootCommandAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var input = parseResult.GetValue(InputArgument);
        var output = parseResult.GetValue(OutputOption);
        var config = parseResult.GetValue(ConfigOption);
        var level = parseResult.GetValue(LevelOption);
        var stringEncrypt = parseResult.GetValue(StringEncryptOption);
        var controlFlow = parseResult.GetValue(ControlFlowOption);
        var rename = parseResult.GetValue(RenameOption);
        var antiDebug = parseResult.GetValue(AntiDebugOption);
        var antiTamper = parseResult.GetValue(AntiTamperOption);
        var antiDecompiler = parseResult.GetValue(AntiDecompilerOption);
        var antiDump = parseResult.GetValue(AntiDumpOption);
        var referenceProxy = parseResult.GetValue(ReferenceProxyOption);
        var proxyExternal = parseResult.GetValue(ProxyExternalOption);
        var encryptMethods = parseResult.GetValue(EncryptMethodsOption);
        var noStringEncrypt = parseResult.GetValue(NoStringEncryptOption);
        var noRename = parseResult.GetValue(NoRenameOption);
        var noControlFlow = parseResult.GetValue(NoControlFlowOption);
        var stripMetadata = parseResult.GetValue(StripMetadataOption);
        var encryptResources = parseResult.GetValue(EncryptResourcesOption);
        var encryptConstants = parseResult.GetValue(EncryptConstantsOption);
        var preservePublic = parseResult.GetValue(PreservePublicOption);
        var map = parseResult.GetValue(MapOption);
        var report = parseResult.GetValue(ReportOption);
        var dryRun = parseResult.GetValue(DryRunOption);
        var verbose = parseResult.GetValue(VerboseOption);
        var noLogo = parseResult.GetValue(NoLogoOption);
        var merge = parseResult.GetValue(MergeOption);
        var internalize = parseResult.GetValue(InternalizeOption);
        var watermarkId = parseResult.GetValue(WatermarkIdOption);
        var virtualize = parseResult.GetValue(VirtualizeOption);
        var incremental = parseResult.GetValue(IncrementalOption);

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
                antiDump, referenceProxy, encryptConstants, noControlFlow, encryptMethods, proxyExternal,
                watermarkId, virtualize, incremental).ConfigureAwait(false);

            if (merge)
            {
                settings.AssemblyMerge.Enabled = true;
                settings.AssemblyMerge.Internalize = internalize;
            }

            return await RunObfuscationAsync(
                input ?? [], output, settings, map, report, dryRun, verbose, merge, preservePublic, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or InvalidOperationException or JsonException or XmlException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    private static void PrintBanner()
    {
        AnsiConsole.Write(new FigletText("Obfy")
            .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[dim]C# Obfuscation Tool[/]");
        AnsiConsole.WriteLine();
    }

    internal static async Task<ObfySettings> BuildSettingsAsync(
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
        bool noControlFlow = false,
        bool encryptMethods = false,
        bool proxyExternal = false,
        string? watermarkId = null,
        bool virtualize = false,
        bool incremental = false)
    {
        ObfySettings settings;

        if (configFile != null)
        {
            if (!configFile.Exists)
                throw new FileNotFoundException($"Config file not found: {configFile.FullName}");

            var json = await File.ReadAllTextAsync(configFile.FullName).ConfigureAwait(false);
            settings = JsonSerializer.Deserialize<ObfySettings>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
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

        if (watermarkId is not null && string.IsNullOrWhiteSpace(watermarkId))
            throw new ArgumentException("--watermark-id requires a non-whitespace identifier.");

        var watermarkRequested = !string.IsNullOrWhiteSpace(watermarkId);
        var anyOverride = stringEncrypt || controlFlow || rename || antiDebug || stripMetadata
            || encryptResources || preservePublic || antiTamper || antiDecompiler
            || noStringEncrypt || noRename
            || antiDump || referenceProxy || encryptConstants || noControlFlow || encryptMethods || proxyExternal
            || watermarkRequested || virtualize || incremental;

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
        if (proxyExternal)
        {
            settings.Protection.ReferenceProxy = true;
            settings.Protection.ProxyExternalCalls = true;
        }
        if (encryptMethods) settings.Protection.MethodEncryption = true;
        if (stripMetadata) settings.Metadata.RemoveDebugInfo = true;
        if (encryptResources) settings.ResourceEncryption.Enabled = true;
        if (encryptConstants) settings.ConstantEncryption.Enabled = true;
        if (preservePublic) settings.SymbolRenaming.PreservePublicApi = true;
        if (watermarkRequested)
        {
            settings.Watermark.Enabled = true;
            settings.Watermark.Id = watermarkId!.Trim();
        }
        if (virtualize) settings.Virtualization.Enabled = true;
        if (incremental) settings.Incremental.Enabled = true;

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
        bool merge = false,
        bool preservePublic = false,
        CancellationToken cancellationToken = default)
    {
        var solutionCount = inputs.Count(static i => IsSolutionFile(i.FullName));
        if (solutionCount > 1)
        {
            AnsiConsole.MarkupLine("[red]Only one solution file can be obfuscated per run.[/]");
            return 1;
        }

        var builder = new ContainerBuilder();
        builder.RegisterModule(new LoggingModule("Obfy.Console", enableConsoleOutput: verbose));
        builder.RegisterModule<ObfuscationModule>();
        await using var container = builder.Build();

        var service = container.Resolve<IObfuscationService>();
        var reportService = container.Resolve<IReportService>();
        var analyzer = container.Resolve<ISolutionAnalyzer>();

        var allSymbols = new Dictionary<string, string>();
        var successfulResults = new List<(ObfuscationResult Result, ObfySettings Settings)>();
        var anyFailed = false;

        if (inputs.Any(static i => IsSolutionOrProject(i.FullName)))
        {
            return await RunClosedSetSessionAsync(
                inputs, output, settings, mapFile, reportFile, dryRun, preservePublic, merge,
                service, reportService, analyzer, cancellationToken).ConfigureAwait(false);
        }

        if (merge && inputs.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Merge requires at least two input assemblies.[/]");
            return 1;
        }

        if (merge)
        {
            anyFailed = await RunMergeObfuscationAsync(inputs, output, settings, allSymbols, successfulResults, service, dryRun).ConfigureAwait(false);
        }
        else if (ShouldUseLooseClosedSet(inputs))
        {
            return await RunLooseClosedSetAsync(
                inputs, output, settings, mapFile, reportFile, dryRun, preservePublic,
                service, reportService, cancellationToken).ConfigureAwait(false);
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

    internal static string FormatLibraryMode(
        bool included,
        bool hintPreservePublic,
        bool forcePreservePublic,
        bool extraUnknownUntilLoad = false)
    {
        if (!included)
            return "No";
        if (forcePreservePublic || hintPreservePublic)
            return "Yes";
        if (extraUnknownUntilLoad)
            return "After load";
        return "No";
    }

    private static async Task<int> RunClosedSetSessionAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        FileInfo? mapFile,
        FileInfo? reportFile,
        bool dryRun,
        bool preservePublic,
        bool merge,
        IObfuscationService service,
        IReportService reportService,
        ISolutionAnalyzer analyzer,
        CancellationToken cancellationToken)
    {
        var sessionPaths = new List<FileInfo>();
        var extraAssemblies = new List<FileInfo>();
        var skippedSource = false;

        foreach (var input in inputs)
        {
            if (IsSolutionOrProject(input.FullName))
            {
                sessionPaths.Add(input);
                continue;
            }

            if (IsAssemblyFile(input.FullName))
            {
                extraAssemblies.Add(input);
                continue;
            }

            if (IsSourceFile(input.FullName))
                skippedSource = true;
        }

        var entries = new List<ProjectProtectionEntry>();
        foreach (var path in sessionPaths)
        {
            var analyzed = analyzer.Analyze(path.FullName);
            entries.AddRange(analyzed.Entries);
        }

        var session = new ProtectionSession
        {
            SourcePath = sessionPaths[0].FullName,
            Entries = entries
        };

        if (skippedSource)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Source files are not part of a solution session and were skipped.[/]");
        }

        var presentExtras = extraAssemblies.Where(static e => File.Exists(e.FullName)).ToList();
        var missingExtras = extraAssemblies.Where(static e => !File.Exists(e.FullName)).ToList();
        foreach (var missing in missingExtras)
            AnsiConsole.MarkupLine($"[red]File not found: {Markup.Escape(missing.FullName)}[/]");

        PrintSessionPlan(session, presentExtras, missingExtras, preservePublic);

        if (missingExtras.Count > 0)
            return 1;

        var closedSetInputs = new List<ClosedSetInput>();
        foreach (var entry in session.Included)
            closedSetInputs.Add(ClosedSetInput.FromIncluded(entry));

        foreach (var extra in presentExtras)
            closedSetInputs.Add(ClosedSetInput.From(extra.FullName));

        if (closedSetInputs.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No built outputs found. Build the ship set (Release) and retry.[/]");
            return 2;
        }

        var useMerge = merge && closedSetInputs.Count >= 2;
        if (merge && !useMerge)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Merge requires at least two included assemblies; continuing as a closed set.[/]");
        }

        if (useMerge)
            AnsiConsole.MarkupLine("[cyan]Merging included assemblies into one before obfuscating.[/]");

        if (dryRun)
            return 0;

        var outputDir = output?.FullName;
        if (string.IsNullOrEmpty(outputDir))
        {
            var solutionDir = Path.GetDirectoryName(Path.GetFullPath(session.SourcePath))
                ?? Directory.GetCurrentDirectory();
            outputDir = Path.Combine(solutionDir, "obfy-out");
            AnsiConsole.MarkupLine($"[cyan]Output directory:[/] {Markup.Escape(outputDir)}");
        }

        if (useMerge)
        {
            var outputPath = Path.Combine(outputDir, Path.GetFileName(closedSetInputs[0].AssemblyPath));
            var mergeResult = await service.MergeAndObfuscateAsync(
                closedSetInputs.Select(static i => i.AssemblyPath).ToArray(),
                outputPath,
                settings,
                cancellationToken).ConfigureAwait(false);

            if (!mergeResult.Success)
            {
                DisplayError("Merge", mergeResult);
                return 1;
            }

            DisplaySuccess($"Merged ({closedSetInputs.Count} assemblies)", mergeResult);
            await WriteMapAndReportAsync(mapFile, reportFile, mergeResult.SymbolMap, mergeResult, settings, service, reportService)
                .ConfigureAwait(false);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Obfuscation complete![/]");
            return 0;
        }

        var result = await service.ObfuscateClosedSetAsync(
            closedSetInputs, outputDir, settings, forcePreservePublic: preservePublic, cancellationToken)
            .ConfigureAwait(false);

        return await WriteClosedSetOutcomeAsync(
            result, mapFile, reportFile, settings, service, reportService).ConfigureAwait(false);
    }

    private static async Task<int> RunLooseClosedSetAsync(
        FileInfo[] inputs,
        DirectoryInfo? output,
        ObfySettings settings,
        FileInfo? mapFile,
        FileInfo? reportFile,
        bool dryRun,
        bool preservePublic,
        IObfuscationService service,
        IReportService reportService,
        CancellationToken cancellationToken)
    {
        var present = inputs.Where(static i => IsAssemblyFile(i.FullName) && File.Exists(i.FullName)).ToList();
        var missing = inputs.Where(static i => IsAssemblyFile(i.FullName) && !File.Exists(i.FullName)).ToList();
        var skippedSource = new List<FileInfo>();
        var skippedOther = new List<FileInfo>();
        foreach (var input in inputs)
        {
            if (IsAssemblyFile(input.FullName) || IsSolutionOrProject(input.FullName))
                continue;
            if (IsSourceFile(input.FullName))
                skippedSource.Add(input);
            else
                skippedOther.Add(input);
        }

        foreach (var file in missing)
            AnsiConsole.MarkupLine($"[red]File not found: {Markup.Escape(file.FullName)}[/]");
        foreach (var file in skippedSource)
            AnsiConsole.MarkupLine(
                $"[yellow]Source files are not part of a closed set and were skipped: {Markup.Escape(file.FullName)}[/]");
        foreach (var file in skippedOther)
            AnsiConsole.MarkupLine(
                $"[yellow]Ignoring non-assembly input: {Markup.Escape(file.FullName)}[/]");

        if (present.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No assemblies found.[/]");
            return 2;
        }

        var hints = new ProjectSettingsHints();
        var entries = present.Select(f => ProjectProtectionEntry.Included(
            f.FullName,
            Path.GetFileNameWithoutExtension(f.Name),
            f.FullName,
            hints)).ToList();

        var session = new ProtectionSession
        {
            SourcePath = present[0].FullName,
            Entries = entries
        };

        PrintSessionPlan(session, [], missing, preservePublic);

        if (missing.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]Closed-set obfuscation requires every listed assembly to exist.[/]");
            return 1;
        }

        var closedSetInputs = session.Included.Select(ClosedSetInput.FromIncluded).ToList();

        if (dryRun)
            return 0;

        var outputDir = output?.FullName;
        if (string.IsNullOrEmpty(outputDir))
        {
            var firstDir = Path.GetDirectoryName(Path.GetFullPath(present[0].FullName))
                ?? Directory.GetCurrentDirectory();
            outputDir = Path.Combine(firstDir, "obfy-out");
            AnsiConsole.MarkupLine($"[cyan]Output directory:[/] {Markup.Escape(outputDir)}");
        }

        var result = await service.ObfuscateClosedSetAsync(
            closedSetInputs, outputDir, settings, forcePreservePublic: preservePublic, cancellationToken)
            .ConfigureAwait(false);

        return await WriteClosedSetOutcomeAsync(
            result, mapFile, reportFile, settings, service, reportService).ConfigureAwait(false);
    }

    private static async Task<int> WriteClosedSetOutcomeAsync(
        ClosedSetResult result,
        FileInfo? mapFile,
        FileInfo? reportFile,
        ObfySettings settings,
        IObfuscationService service,
        IReportService reportService)
    {
        foreach (var failure in result.LoadFailures)
        {
            AnsiConsole.MarkupLine(
                $"[red]Failed to load {Markup.Escape(failure.Path)}: {Markup.Escape(failure.Message)}[/]");
        }

        if (!result.Success)
        {
            AnsiConsole.MarkupLine(
                $"[red]{Markup.Escape(result.ErrorMessage ?? "Closed-set obfuscation failed.")}[/]");
            foreach (var module in result.ModuleResults.Where(static m => !m.Success))
                DisplayError(Path.GetFileName(module.InputPath) ?? "module", module);
            return 1;
        }

        foreach (var module in result.ModuleResults)
        {
            var name = Path.GetFileName(module.OutputPath ?? module.InputPath) ?? "module";
            DisplaySuccess(name, module);
        }

        if (reportFile != null && result.ModuleResults.Count > 1)
        {
            var firstName = Path.GetFileName(result.ModuleResults[0].InputPath) ?? "module";
            AnsiConsole.MarkupLine(
                $"[yellow]Report contains the first module only ({Markup.Escape(firstName)}); " +
                $"{result.ModuleResults.Count - 1} other module(s) omitted.[/]");
        }

        var reportSource = result.ModuleResults.Count > 0 ? result.ModuleResults[0] : null;
        await WriteMapAndReportAsync(mapFile, reportFile, result.SymbolMap, reportSource, settings, service, reportService)
            .ConfigureAwait(false);

        AnsiConsole.WriteLine();
        if (result.LoadFailures.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]Closed-set finished with load failures. Remaining modules were written.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Obfuscation complete![/]");
        return 0;
    }

    private static async Task WriteMapAndReportAsync(
        FileInfo? mapFile,
        FileInfo? reportFile,
        Dictionary<string, string> symbolMap,
        ObfuscationResult? reportSource,
        ObfySettings settings,
        IObfuscationService service,
        IReportService reportService)
    {
        if (mapFile != null && symbolMap.Count > 0)
        {
            await service.WriteSymbolMapAsync(symbolMap, mapFile.FullName).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Symbol map written to {mapFile.FullName}[/]");
        }

        if (reportFile != null && reportSource != null)
        {
            var format = Path.GetExtension(reportFile.FullName).ToLowerInvariant() == ".json"
                ? ReportFormat.Json
                : ReportFormat.Html;
            var report = reportService.BuildReport(reportSource, settings);
            await reportService.GenerateReportAsync(report, reportFile.FullName, format).ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[green]Report written to {reportFile.FullName}[/]");
        }
    }

    private static void PrintSessionPlan(
        ProtectionSession session,
        IReadOnlyList<FileInfo> presentExtras,
        IReadOnlyList<FileInfo> missingExtras,
        bool forcePreservePublic)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Project")
            .AddColumn("Status")
            .AddColumn("Output")
            .AddColumn("Library-mode")
            .AddColumn("RuntimeProfile");

        foreach (var entry in session.Entries)
        {
            var status = entry.IsIncluded
                ? "Included"
                : entry.SkipMessage ?? entry.SkipReason.ToString();
            table.AddRow(
                Markup.Escape(entry.ProjectName),
                Markup.Escape(status),
                Markup.Escape(entry.OutputPath ?? ""),
                FormatLibraryMode(entry.IsIncluded, entry.Hints.PreservePublicApi, forcePreservePublic),
                entry.Hints.RuntimeProfile.ToString());
        }

        foreach (var extra in presentExtras)
        {
            table.AddRow(
                Markup.Escape(Path.GetFileNameWithoutExtension(extra.Name)),
                "Included",
                Markup.Escape(extra.FullName),
                FormatLibraryMode(included: true, hintPreservePublic: false, forcePreservePublic, extraUnknownUntilLoad: true),
                RuntimeProfile.Default.ToString());
        }

        foreach (var extra in missingExtras)
        {
            table.AddRow(
                Markup.Escape(Path.GetFileNameWithoutExtension(extra.Name)),
                "Missing",
                Markup.Escape(extra.FullName),
                FormatLibraryMode(included: false, hintPreservePublic: false, forcePreservePublic),
                RuntimeProfile.Default.ToString());
        }

        AnsiConsole.Write(table);
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
        AnsiConsole.MarkupLine($"[red]Failed to obfuscate {Markup.Escape(fileName)}[/]");
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(result.ErrorMessage ?? "")}[/]");

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
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(settings, options);
        var node = JsonNode.Parse(json)!.AsObject();
        node.Insert(0, "$schema", "https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json");
        await File.WriteAllTextAsync(output.FullName, node.ToJsonString(options)).ConfigureAwait(false);

        AnsiConsole.MarkupLine($"[green]Configuration file created: {output.FullName}[/]");
    }
}
