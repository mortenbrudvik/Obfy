using System.ComponentModel.DataAnnotations;
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
/// all-or-nothing write. A member that fails to load fails the run before renaming, so the
/// rest of the set is not rewritten against an incomplete assembly list.
/// Settings follow the single-file rules: cloned, a non-Custom level is applied, then validated. The log
/// carries counts, module numbers and exception types, never paths or exception text.
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

        // Same rules as ObfuscationService.ObfuscateAsync: a copy (the caller's object is never mutated), the
        // level applied unless the caller passed Custom (the CLI and UI do, so explicit flags win), then validated.
        // A failure is a result, not an exception.
        try
        {
            baseSettings = baseSettings.Clone();
            if (baseSettings.Level != ObfuscationLevel.Custom)
                baseSettings.ApplyLevel();
            baseSettings.Validate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Invalid obfuscation settings ({ExceptionType})", ex.GetType().Name);
            return ClosedSetResult.Failed($"Invalid settings: {ex.Message}");
        }

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

            if (loadFailures.Count > 0)
            {
                return ClosedSetResult.Failed(
                    $"Closed-set obfuscation stopped because {loadFailures.Count} assembly(ies) failed to load.",
                    loadFailures);
            }

            var protectedMembers = loaded
                .Where(m => ProtectedInputGuard.ContainsInjectedHelpers(m.Module))
                .Select(m => Path.GetFileName(m.Input.AssemblyPath))
                .ToList();
            if (protectedMembers.Count > 0)
            {
                _logger.LogWarning(
                    "{Count} closed-set member(s) already contain Obfy runtime helper types; run refused",
                    protectedMembers.Count);
                return ClosedSetResult.Failed(
                    $"Closed-set obfuscation stopped: {string.Join(", ", protectedMembers)} "
                    + (protectedMembers.Count == 1 ? "is" : "are")
                    + " already protected (Obfy runtime helper types are present). Running Obfy on its own output "
                    + "would inject them twice and can corrupt the result. Protect the original builds instead.",
                    loadFailures);
            }

            var hasEntryPoint = loaded.Any(static m => HasEntryPoint(m.Module));
            var referencedByExe = FindAssembliesReferencedByEntryPoints(loaded);

            var renamePairs = new List<(ModuleDef Module, ObfySettings Settings)>(loaded.Count);
            var jobs = new List<ModuleJob>(loaded.Count);
            var relativeOutputs = AssignRelativeOutputPaths(loaded);

            var collision = FindOutputCollision(loaded, relativeOutputs);
            if (collision is not null)
                return ClosedSetResult.Failed(collision, loadFailures);

            if (baseSettings.Signing.Enabled)
            {
                try
                {
                    RewriteReferenceTokens(loaded, AssemblySigner.LoadKey(baseSettings.Signing));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError("Could not load the signing key ({ExceptionType})", ex.GetType().Name);
                    return ClosedSetResult.Failed($"Failed to load the signing key: {ex.Message}", loadFailures);
                }
            }

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
                // Gating warnings are fixed texts without user data.
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
                    _logger.LogError("Closed-set symbol renaming failed ({ExceptionType})", ex.GetType().Name);
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
                if (i == 0)
                {
                    foreach (var warning in sessionContext.Warnings)
                        context.Warnings.Add(warning);
                    foreach (var pair in sessionContext.SymbolMap)
                        context.SymbolMap[pair.Key] = pair.Value;
                    context.Statistics.Merge(sessionContext.Statistics);
                    context.SkippedItems.AddRange(sessionContext.SkippedItems);

                    // The cache covers a single assembly or a merge only. One note per run, so on the first module.
                    if (baseSettings.Incremental.Enabled)
                    {
                        context.Warnings.Add(ObfuscationWarnings.ClosedSetCacheNote);
                        _logger.LogInformation("Incremental cache not used: a closed set is never cached");
                    }
                }

                var result = await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    // The error text can name symbols or paths from the input, so it is not logged.
                    _logger.LogError("Closed-set pipeline failed for module {Index} of {Count}", i + 1, jobs.Count);
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

            if (jobs.Sum(static j => j.Context!.Statistics.TotalTransformations) == 0)
            {
                // Renaming runs once for the whole set and is counted on the first module, so only the total says
                // whether anything happened.
                foreach (var job in jobs)
                    job.Context!.Warnings.Add(ObfuscationWarnings.NoTransformations);
            }

            tempDir = Path.Combine(Path.GetTempPath(), $"obfy-closed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
            {
                var job = jobs[jobIndex];
                cancellationToken.ThrowIfCancellationRequested();
                var tempPath = Path.Combine(tempDir, job.RelativeOutput);
                job.Context!.OutputPath = tempPath;
                // SaveAsync disposes and nulls job.Context.Module, so inspect the entry point beforehand.
                var jobHasEntryPoint = HasEntryPoint(job.Loaded.Module);
                try
                {
                    await _assemblyProcessor.SaveAsync(job.Context, tempPath, cancellationToken).ConfigureAwait(false);
                    job.Loaded.Saved = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        "Failed to save closed-set module {Index} of {Count} ({ExceptionType})",
                        jobIndex + 1, jobs.Count, ex.GetType().Name);
                    return ClosedSetResult.Failed(
                        $"Failed to save output: {ex.Message}",
                        loadFailures,
                        moduleResults,
                        symbolMap);
                }

                if (!job.PipelineSettings.Packing.Enabled)
                    continue;

                if (!jobHasEntryPoint)
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
                    _logger.LogInformation("Packed closed-set module {Index} of {Count}", jobIndex + 1, jobs.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        "Packing failed for closed-set module {Index} of {Count} ({ExceptionType})",
                        jobIndex + 1, jobs.Count, ex.GetType().Name);
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
            {
                if (!item.Saved)
                    item.Module.Dispose();
            }

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
                _logger.LogWarning("Failed to load a closed-set member ({ExceptionType})", ex.GetType().Name);
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

    /// <summary>
    /// Every member is re-signed with the same key, so its public key token changes. A reference from one
    /// member to another must name the new token or the CLR (.NET Framework) refuses to bind it, and
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> targeting a member of the set
    /// must carry the new public key so friend access continues to work after signing.
    /// </summary>
    private static void RewriteReferenceTokens(List<LoadedModule> loaded, StrongNameKey key)
    {
        var token = new PublicKey(key.PublicKey).Token;
        var publicKeyHex = BitConverter.ToString(key.PublicKey).Replace("-", string.Empty).ToLowerInvariant();
        var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded)
        {
            if (item.Module.Assembly?.Name?.String is { Length: > 0 } name)
                memberNames.Add(name);
        }

        foreach (var item in loaded)
        {
            foreach (var assemblyRef in item.Module.GetAssemblyRefs())
            {
                if (assemblyRef.Name?.String is { } refName && memberNames.Contains(refName))
                    assemblyRef.PublicKeyOrToken = token;
            }

            RewriteInternalsVisibleTo(item.Module.Assembly, memberNames, publicKeyHex);
        }
    }

    private static void RewriteInternalsVisibleTo(
        AssemblyDef? assembly,
        HashSet<string> memberNames,
        string publicKeyHex)
    {
        if (assembly is null)
            return;

        foreach (var attr in assembly.CustomAttributes)
        {
            if (!string.Equals(
                    attr.TypeFullName,
                    "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (attr.ConstructorArguments.Count == 0)
                continue;

            var current = attr.ConstructorArguments[0].Value switch
            {
                UTF8String utf => utf.String,
                string text => text,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(current))
                continue;

            var comma = current.IndexOf(',');
            var friendName = (comma < 0 ? current : current[..comma]).Trim();
            if (!memberNames.Contains(friendName))
                continue;

            var arg = attr.ConstructorArguments[0];
            attr.ConstructorArguments[0] = new CAArgument(
                arg.Type,
                new UTF8String($"{friendName}, PublicKey={publicKeyHex}"));
        }
    }

    private static HashSet<string> FindAssembliesReferencedByEntryPoints(List<LoadedModule> loaded)
    {
        var byName = new Dictionary<string, List<LoadedModule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded)
        {
            if (item.Module.Assembly?.Name?.String is not { Length: > 0 } name)
                continue;
            if (!byName.TryGetValue(name, out var list))
            {
                list = [];
                byName[name] = list;
            }

            list.Add(item);
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<LoadedModule>();
        var visited = new HashSet<LoadedModule>();

        foreach (var item in loaded)
        {
            if (HasEntryPoint(item.Module) && visited.Add(item))
                queue.Enqueue(item);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var selfName = current.Module.Assembly?.Name?.String;
            foreach (var assemblyRef in current.Module.GetAssemblyRefs())
            {
                var refName = assemblyRef.Name?.String;
                if (string.IsNullOrEmpty(refName) || !byName.TryGetValue(refName, out var targets))
                    continue;
                if (selfName is not null && refName.Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    continue;

                referenced.Add(refName);
                foreach (var target in targets)
                {
                    if (visited.Add(target))
                        queue.Enqueue(target);
                }
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
            _logger.LogError("Failed to commit closed-set output ({ExceptionType})", ex.GetType().Name);
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
                {
                    File.Copy(backup, dest, overwrite: true);
                    // Restored: the backup has done its job. A backup that could not be restored stays for the user.
                    DeleteFile(backup);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to restore a closed-set output from its backup ({ExceptionType})", ex.GetType().Name);
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
            try
            {
                File.Copy(dest, backup, overwrite: true);
            }
            catch
            {
                // A half-written backup must not be left behind, and must never be restored over the output.
                try
                {
                    File.Delete(backup);
                }
                catch (Exception)
                {
                    // Best effort: the original failure is the one to report.
                }

                throw;
            }

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
        => PlanRelativeOutputPaths([.. loaded.Select(static m => m.Input.AssemblyPath)]);

    /// <summary>
    /// Where each assembly of a closed set lands under the output folder, relative to it, in input order: its file name,
    /// or <c>{tfm or parent folder}\{file name}</c> when two assemblies share a name. The one rule the run writes by, so a
    /// caller can plan the outputs without running anything.
    /// </summary>
    public static IReadOnlyList<string> PlanRelativeOutputPaths(IReadOnlyList<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyPath in assemblyPaths)
        {
            var fileName = Path.GetFileName(assemblyPath);
            counts[fileName] = counts.GetValueOrDefault(fileName) + 1;
        }

        var relative = new string[assemblyPaths.Count];
        for (var i = 0; i < assemblyPaths.Count; i++)
        {
            var path = assemblyPaths[i];
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
            _logger.LogWarning("Failed to delete the closed-set temp directory ({ExceptionType})", ex.GetType().Name);
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
            _logger.LogWarning("Failed to delete a closed-set backup file ({ExceptionType})", ex.GetType().Name);
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
