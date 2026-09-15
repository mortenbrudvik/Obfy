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
/// all-or-nothing write. Pipeline/save/commit is all-or-nothing; modules that fail to load
/// are omitted and the remaining set may still commit.
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
        var loadFailures = new List<ClosedSetLoadFailure>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadRemainingAsync(inputs, loaded, loadFailures, cancellationToken).ConfigureAwait(false);

            if (loaded.Count == 0)
            {
                return ClosedSetResult.Failed("No assemblies could be loaded.", loadFailures);
            }

            var hasEntryPoint = loaded.Any(static m => HasEntryPoint(m.Module));
            var referencedByExe = FindAssembliesReferencedByEntryPoints(loaded);

            var renamePairs = new List<(ModuleDef Module, ObfySettings Settings)>(loaded.Count);
            var jobs = new List<ModuleJob>(loaded.Count);
            var relativeOutputs = AssignRelativeOutputPaths(loaded);

            var collision = FindOutputCollision(loaded, relativeOutputs);
            if (collision is not null)
                return ClosedSetResult.Failed(collision, loadFailures);

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
            {
                try
                {
                    _renamer.RenameClosedSet(renamePairs, sessionContext, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Closed-set symbol renaming failed");
                    return ClosedSetResult.Failed(
                        $"Closed-set symbol renaming failed: {ex.Message}",
                        loadFailures);
                }
            }

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
                    return ClosedSetResult.Failed(
                        result.ErrorMessage ?? "Pipeline failed.",
                        loadFailures,
                        moduleResults,
                        symbolMap);
                }

                foreach (var pair in context.SymbolMap)
                    symbolMap[pair.Key] = pair.Value;

                job.Context = context;
                job.Elapsed = result.ElapsedTime;
                moduleResults.Add(ToSuccessfulModuleResult(job, outputPath: null, packedLauncherPath: null));
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
                    return ClosedSetResult.Failed(
                        $"Failed to save output: {ex.Message}",
                        loadFailures,
                        moduleResults,
                        symbolMap);
                }

                if (!job.PipelineSettings.Packing.Enabled)
                    continue;

                if (!HasEntryPoint(job.Loaded.Module))
                {
                    job.Context.Warnings.Add("Packing skipped: assembly has no entry point.");
                    continue;
                }

                try
                {
                    job.PackedPath = PackingApplicator.Pack(
                        tempPath,
                        job.PipelineSettings,
                        job.Loaded.Input.AssemblyPath,
                        job.Context.Warnings);
                    _logger.LogInformation("Packed closed-set output {Path}", job.PackedPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Packing failed for {Path}", job.Loaded.Input.AssemblyPath);
                    return ClosedSetResult.Failed(
                        $"Packing failed: {ex.Message}",
                        loadFailures,
                        moduleResults,
                        symbolMap);
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
                item.Module.Dispose();

            DeleteDirectory(tempDir);
        }
    }

    private async Task LoadRemainingAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        List<LoadedModule> loaded,
        List<ClosedSetLoadFailure> loadFailures,
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
                module.Location = input.AssemblyPath;
                resolver.AddToCache(module);
                loaded.Add(new LoadedModule(input, module));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to load {Path}", input.AssemblyPath);
                loadFailures.Add(new ClosedSetLoadFailure
                {
                    Path = input.AssemblyPath,
                    Message = ex.Message
                });
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
        var settings = SolutionHintApplier.Overlay(baseSettings, item.Input.Hints, forcePreservePublic);

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
        List<ClosedSetLoadFailure> loadFailures,
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
                PlaceFile(source, dest, outputDirectory, stashes, placed);
                foreach (var sidecar in PackingApplicator.SidecarPaths(source, job.PipelineSettings.Packing))
                {
                    if (!File.Exists(sidecar))
                        continue;
                    var destSidecar = Path.Combine(
                        Path.GetDirectoryName(dest) ?? outputDirectory,
                        Path.GetFileName(sidecar));
                    PlaceFile(sidecar, destSidecar, outputDirectory, stashes, placed);
                }
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
            var restoreErrors = RestoreCommit(placed, stashes);
            var message = $"Failed to save output: {ex.Message}";
            if (restoreErrors.Count > 0)
            {
                message += ". Restore also failed; backups left as *.obfyprev: "
                    + string.Join("; ", restoreErrors);
            }

            return ClosedSetResult.Failed(message, loadFailures, moduleResults, symbolMap);
        }

        foreach (var (_, backup) in stashes)
            DeleteFile(backup);

        var finalResults = new List<ObfuscationResult>(jobs.Count);
        foreach (var job in jobs)
        {
            var dest = Path.Combine(outputDirectory, job.RelativeOutput);
            finalResults.Add(ToSuccessfulModuleResult(job, dest, PackedPathForCommit(job, dest)));
        }

        return ClosedSetResult.Succeeded(finalResults, loadFailures, symbolMap);
    }

    private List<string> RestoreCommit(List<string> placed, List<(string Dest, string Backup)> stashes)
    {
        var errors = new List<string>();

        foreach (var path in placed)
        {
            if (stashes.Exists(s => string.Equals(s.Dest, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            DeleteFile(path);
        }

        foreach (var (dest, backup) in stashes)
        {
            try
            {
                if (File.Exists(backup))
                    File.Copy(backup, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore {Path} from {Backup}", dest, backup);
                errors.Add($"{dest} (backup: {backup}): {ex.Message}");
            }
        }

        return errors;
    }

    private static void PlaceFile(
        string source,
        string dest,
        string outputDirectory,
        List<(string Dest, string Backup)> stashes,
        List<string> placed)
    {
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        if (File.Exists(dest))
        {
            var backup = Path.Combine(
                destDir ?? outputDirectory,
                $".{Path.GetFileName(dest)}.{Guid.NewGuid():N}.obfyprev");
            File.Copy(dest, backup, overwrite: true);
            stashes.Add((dest, backup));
        }

        File.Copy(source, dest, overwrite: true);
        placed.Add(dest);
    }

    private static string? PackedPathForCommit(ModuleJob job, string dest)
    {
        if (job.PackedPath is null)
            return null;
        return job.PipelineSettings.Packing.IsPortable
            ? ManagedLauncherPacker.LauncherPathFor(dest)
            : dest;
    }

    private static ObfuscationResult ToSuccessfulModuleResult(
        ModuleJob job, string? outputPath, string? packedLauncherPath)
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
            warnings: [.. context.Warnings],
            packedLauncherPath: packedLauncherPath);
    }

    private static string? FindOutputCollision(List<LoadedModule> loaded, IReadOnlyList<string> relativeOutputs)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < loaded.Count; i++)
        {
            if (seen.TryGetValue(relativeOutputs[i], out var first))
            {
                return $"Output path collision '{relativeOutputs[i]}' for '{first}' and '{loaded[i].Input.AssemblyPath}'.";
            }

            seen[relativeOutputs[i]] = loaded[i].Input.AssemblyPath;
        }

        return null;
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

            var tfm = ClosedSetPath.FindTfmSegment(path);
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

    private static bool HasEntryPoint(ModuleDef module)
        => module.EntryPoint is not null
            || module.Kind is ModuleKind.Console or ModuleKind.Windows;

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
        public string? PackedPath { get; set; }
    }
}
