using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Default closed-set coordinator: one module context, one rename pass, per-module pipeline,
/// all-or-nothing write.
/// </summary>
public class ClosedSetProcessor : IClosedSetProcessor
{
    private readonly ILogger<ClosedSetProcessor> _logger;
    private readonly IObfuscationPipeline _pipeline;
    private readonly IAssemblyProcessor _assemblyProcessor;
    private readonly SymbolRenamingObfuscator _renamer;

    public ClosedSetProcessor(
        ILogger<ClosedSetProcessor> logger,
        IObfuscationPipeline pipeline,
        IAssemblyProcessor assemblyProcessor,
        SymbolRenamingObfuscator renamer)
    {
        _logger = logger;
        _pipeline = pipeline;
        _assemblyProcessor = assemblyProcessor;
        _renamer = renamer;
    }

    /// <inheritdoc/>
    public async Task<ClosedSetResult> ExecuteAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings baseSettings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(baseSettings);

        string? tempDir = null;
        var loaded = new List<LoadedModule>();
        var loadFailures = new List<string>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadRemainingAsync(inputs, loaded, loadFailures, cancellationToken).ConfigureAwait(false);

            if (loaded.Count == 0)
            {
                return new ClosedSetResult
                {
                    Success = false,
                    ErrorMessage = "No assemblies could be loaded.",
                    LoadFailures = loadFailures
                };
            }

            var hasEntryPoint = loaded.Any(static m => HasEntryPoint(m.Module));
            var referencedByExe = FindAssembliesReferencedByEntryPoints(loaded);

            var renamePairs = new List<(ModuleDef Module, ObfySettings Settings)>(loaded.Count);
            var jobs = new List<ModuleJob>(loaded.Count);
            var relativeOutputs = AssignRelativeOutputPaths(loaded);
            var sessionContext = PipelineContext.ForAssembly(loaded[0].Module, baseSettings.Clone());
            sessionContext.InputPath = loaded[0].Input.AssemblyPath;

            for (var i = 0; i < loaded.Count; i++)
            {
                var item = loaded[i];
                var settings = CloneModuleSettings(
                    baseSettings, item, hasEntryPoint, referencedByExe, forcePreservePublic);

                var gatingContext = PipelineContext.ForAssembly(item.Module, settings);
                gatingContext.InputPath = item.Input.AssemblyPath;
                RuntimeProfileGating.Apply(settings, gatingContext);
                foreach (var warning in gatingContext.Warnings)
                    _logger.LogWarning("{Warning}", warning);

                renamePairs.Add((item.Module, settings));

                var pipelineSettings = settings.Clone();
                pipelineSettings.SymbolRenaming.Enabled = false;
                jobs.Add(new ModuleJob(item, pipelineSettings, relativeOutputs[i], [.. gatingContext.Warnings]));
            }

            if (baseSettings.SymbolRenaming.Enabled)
                _renamer.RenameClosedSet(renamePairs, sessionContext, cancellationToken);

            var symbolMap = new Dictionary<string, string>(sessionContext.SymbolMap);
            var moduleResults = new List<ObfuscationResult>(jobs.Count);

            for (var i = 0; i < jobs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var job = jobs[i];

                var context = PipelineContext.ForAssembly(job.Loaded.Module, job.PipelineSettings);
                context.InputPath = job.Loaded.Input.AssemblyPath;
                context.OutputPath = job.RelativeOutput;
                foreach (var warning in job.GatingWarnings)
                    context.Warnings.Add(warning);
                foreach (var warning in sessionContext.Warnings)
                    context.Warnings.Add(warning);
                foreach (var pair in sessionContext.SymbolMap)
                    context.SymbolMap[pair.Key] = pair.Value;
                if (i == 0)
                {
                    context.Statistics.Merge(sessionContext.Statistics);
                    context.SkippedItems.AddRange(sessionContext.SkippedItems);
                }

                var result = await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    _logger.LogError(
                        "Closed-set pipeline failed for {Path}: {Error}",
                        job.Loaded.Input.AssemblyPath,
                        result.ErrorMessage);
                    moduleResults.Add(result);
                    return Fail(result.ErrorMessage ?? "Pipeline failed.", loadFailures, moduleResults, symbolMap);
                }

                foreach (var pair in context.SymbolMap)
                    symbolMap[pair.Key] = pair.Value;

                job.Context = context;
                job.Elapsed = result.ElapsedTime;
                moduleResults.Add(ToSuccessfulModuleResult(job, outputPath: null));
            }

            tempDir = Path.Combine(Path.GetTempPath(), $"obfy-closed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tempPath = Path.Combine(tempDir, job.RelativeOutput);
                job.Context!.OutputPath = tempPath;
                try
                {
                    await _assemblyProcessor.SaveAsync(job.Context, tempPath, cancellationToken).ConfigureAwait(false);
                    job.Loaded.Saved = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to save {Path}", job.Loaded.Input.AssemblyPath);
                    return Fail($"Failed to save output: {ex.Message}", loadFailures, moduleResults, symbolMap);
                }
            }

            return CommitTempOutput(
                outputDirectory,
                tempDir,
                jobs,
                loadFailures,
                moduleResults,
                symbolMap,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Closed-set processing was cancelled");
            throw;
        }
        finally
        {
            foreach (var item in loaded)
            {
                if (item.Saved)
                    continue;
                item.Module.Dispose();
            }

            DeleteDirectory(tempDir);
        }
    }

    private async Task LoadRemainingAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        List<LoadedModule> loaded,
        List<string> loadFailures,
        CancellationToken cancellationToken)
    {
        var ctx = ModuleDef.CreateModuleContext();
        var resolver = (AssemblyResolver)ctx.AssemblyResolver;

        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = await File.ReadAllBytesAsync(input.AssemblyPath, cancellationToken).ConfigureAwait(false);
                var module = ModuleDefMD.Load(bytes, ctx);
                resolver.AddToCache(module);
                loaded.Add(new LoadedModule(input, module));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to load {Path}", input.AssemblyPath);
                loadFailures.Add(input.AssemblyPath);
            }
        }
    }

    private static ObfySettings CloneModuleSettings(
        ObfySettings baseSettings,
        LoadedModule item,
        bool hasEntryPoint,
        HashSet<string> referencedByExe,
        bool forcePreservePublic)
    {
        var settings = baseSettings.Clone();
        SolutionHintApplier.Apply(settings, item.Input.Hints, forcePreservePublic);

        if (!hasEntryPoint)
            settings.SymbolRenaming.PreservePublicApi = true;
        else if (item.Module.Assembly?.Name?.String is { } assemblyName
                 && referencedByExe.Contains(assemblyName))
        {
            settings.SymbolRenaming.PreservePublicApi = forcePreservePublic;
        }
        else if (!HasEntryPoint(item.Module))
        {
            // Extra assemblies not referenced by an in-set exe stay library-mode.
            settings.SymbolRenaming.PreservePublicApi = true;
        }

        return settings;
    }

    private static HashSet<string> FindAssembliesReferencedByEntryPoints(List<LoadedModule> loaded)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded)
        {
            if (item.Module.Assembly?.Name?.String is { } name)
                names.Add(name);
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded)
        {
            if (!HasEntryPoint(item.Module))
                continue;

            var selfName = item.Module.Assembly?.Name?.String;
            foreach (var assemblyRef in item.Module.GetAssemblyRefs())
            {
                var refName = assemblyRef.Name?.String;
                if (string.IsNullOrEmpty(refName) || !names.Contains(refName))
                    continue;
                if (selfName is not null && refName.Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    continue;
                referenced.Add(refName);
            }
        }

        return referenced;
    }

    private ClosedSetResult CommitTempOutput(
        string outputDirectory,
        string tempDir,
        List<ModuleJob> jobs,
        List<string> loadFailures,
        List<ObfuscationResult> moduleResults,
        Dictionary<string, string> symbolMap,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var stashes = new List<(string Dest, string Backup)>(jobs.Count);
        var placed = new List<string>(jobs.Count);

        try
        {
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(tempDir, job.RelativeOutput);
                var dest = Path.Combine(outputDirectory, job.RelativeOutput);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                if (File.Exists(dest))
                {
                    var backup = Path.Combine(
                        destDir ?? outputDirectory,
                        $".{Path.GetFileName(dest)}.{Guid.NewGuid():N}.obfyprev");
                    File.Move(dest, backup);
                    stashes.Add((dest, backup));
                }

                File.Move(source, dest);
                placed.Add(dest);
            }
        }
        catch (OperationCanceledException)
        {
            RestoreCommit(placed, stashes);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit closed-set output to {Path}", outputDirectory);
            RestoreCommit(placed, stashes);
            return Fail($"Failed to save output: {ex.Message}", loadFailures, moduleResults, symbolMap);
        }

        foreach (var (_, backup) in stashes)
            DeleteFile(backup);

        var finalResults = new List<ObfuscationResult>(jobs.Count);
        foreach (var job in jobs)
        {
            finalResults.Add(ToSuccessfulModuleResult(
                job,
                Path.Combine(outputDirectory, job.RelativeOutput)));
        }

        return new ClosedSetResult
        {
            Success = true,
            ModuleResults = finalResults,
            LoadFailures = loadFailures,
            SymbolMap = symbolMap
        };
    }

    private void RestoreCommit(List<string> placed, List<(string Dest, string Backup)> stashes)
    {
        foreach (var path in placed)
            DeleteFile(path);

        foreach (var (dest, backup) in stashes)
        {
            try
            {
                if (File.Exists(dest))
                    File.Delete(dest);
                if (File.Exists(backup))
                    File.Move(backup, dest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore {Path}", dest);
            }
        }
    }

    private static ObfuscationResult ToSuccessfulModuleResult(ModuleJob job, string? outputPath)
    {
        var context = job.Context!;
        return ObfuscationResult.Successful(
            context.Statistics,
            inputPath: job.Loaded.Input.AssemblyPath,
            outputPath: outputPath,
            elapsedTime: job.Elapsed,
            processingTimes: [.. context.ProcessingTimes],
            skippedItems: [.. context.SkippedItems],
            symbolMap: new Dictionary<string, string>(context.SymbolMap),
            warnings: [.. context.Warnings]);
    }

    private static IReadOnlyList<string> AssignRelativeOutputPaths(List<LoadedModule> loaded)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded)
        {
            var fileName = Path.GetFileName(item.Input.AssemblyPath);
            counts[fileName] = counts.GetValueOrDefault(fileName) + 1;
        }

        var relative = new string[loaded.Count];
        for (var i = 0; i < loaded.Count; i++)
        {
            var path = loaded[i].Input.AssemblyPath;
            var fileName = Path.GetFileName(path);
            if (counts[fileName] == 1)
            {
                relative[i] = fileName;
                continue;
            }

            var tfm = FindTfmSegment(path);
            if (!string.IsNullOrEmpty(tfm))
            {
                relative[i] = Path.Combine(tfm, fileName);
                continue;
            }

            var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)));
            relative[i] = string.IsNullOrEmpty(parent) ? fileName : Path.Combine(parent, fileName);
        }

        return relative;
    }

    private static string? FindTfmSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            if (LooksLikeTfm(segments[i]))
                return segments[i];
        }

        return null;
    }

    // netX.Y or netX.Y-platform (net8.0, net10.0-windows).
    private static bool LooksLikeTfm(string segment)
    {
        if (segment.Length < 5 || !segment.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return false;

        var i = 3;
        if (!char.IsDigit(segment[i]))
            return false;
        while (i < segment.Length && char.IsDigit(segment[i]))
            i++;
        if (i >= segment.Length || segment[i] != '.')
            return false;
        i++;
        return i < segment.Length && char.IsDigit(segment[i]);
    }

    private static bool HasEntryPoint(ModuleDef module)
        => module.EntryPoint is not null
            || module.Kind is ModuleKind.Console or ModuleKind.Windows;

    private static ClosedSetResult Fail(
        string errorMessage,
        IReadOnlyList<string> loadFailures,
        IReadOnlyList<ObfuscationResult>? moduleResults = null,
        Dictionary<string, string>? symbolMap = null)
        => new()
        {
            Success = false,
            ErrorMessage = errorMessage,
            ModuleResults = moduleResults ?? [],
            LoadFailures = loadFailures,
            SymbolMap = symbolMap ?? new Dictionary<string, string>()
        };

    private void DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp directory {Path}", path);
        }
    }

    private void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete {Path}", path);
        }
    }

    private sealed class LoadedModule(ClosedSetInput input, ModuleDefMD module)
    {
        public ClosedSetInput Input { get; } = input;
        public ModuleDefMD Module { get; } = module;
        public bool Saved { get; set; }
    }

    private sealed class ModuleJob(
        LoadedModule loaded,
        ObfySettings pipelineSettings,
        string relativeOutput,
        List<string> gatingWarnings)
    {
        public LoadedModule Loaded { get; } = loaded;
        public ObfySettings PipelineSettings { get; } = pipelineSettings;
        public string RelativeOutput { get; } = relativeOutput;
        public List<string> GatingWarnings { get; } = gatingWarnings;
        public PipelineContext? Context { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
