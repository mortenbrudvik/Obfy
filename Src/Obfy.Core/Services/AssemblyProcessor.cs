using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

/// <summary>
/// Default implementation of assembly processing using dnlib.
/// </summary>
public class AssemblyProcessor : IAssemblyProcessor
{
    private readonly ILogger<AssemblyProcessor> _logger;
    private readonly IReadOnlyList<IPePostProcessor> _postProcessors;

    public AssemblyProcessor(ILogger<AssemblyProcessor> logger, IEnumerable<IPePostProcessor>? postProcessors = null)
    {
        _logger = logger;
        var resolved = postProcessors is null ? null : postProcessors as ICollection<IPePostProcessor> ?? postProcessors.ToList();
        _postProcessors = (resolved is null || resolved.Count == 0
            ?
            [
                new MethodEncryptionPePostProcessor(),
                new AntiTamperPePostProcessor()
            ]
            : resolved).OrderBy(p => p.Order).ToList();
    }

    /// <inheritdoc/>
    public async Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading assembly from {Path}", path);
        cancellationToken.ThrowIfCancellationRequested();

        var moduleContext = ModuleDef.CreateModuleContext();
        // Load from an in-memory byte copy rather than the path directly: ModuleDefMD.Load(path)
        // memory-maps and locks the file for the module's lifetime, which blocks in-place output and
        // leaves the input locked if a later stage fails. Reading the bytes up front avoids the lock.
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var module = ModuleDefMD.Load(bytes, moduleContext);

        _logger.LogDebug("Loaded assembly {Name} with {TypeCount} types",
            module.Name, module.Types.Count);

        var context = PipelineContext.ForAssembly(module, settings);
        context.InputPath = path;

        return context;
    }

    /// <inheritdoc/>
    public Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default)
    {
        if (context.Module == null)
        {
            throw new InvalidOperationException("No module in context");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Saving obfuscated assembly to {Path}", outputPath);

        // Ensure output directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // StrongNameKey on the first write allocates the signature directory. After IL XOR,
        // SignInPlace refreshes the blob. Anti-tamper hashes the whole file except its own
        // slot, so the hash must be patched last (after the final signature).
        var strongNameKey = AssemblySigner.TryLoadKey(context.Settings.Signing);

        var writerOptions = new ModuleWriterOptions(context.Module)
        {
            MetadataOptions = { Flags = MetadataFlags.PreserveAll },
            StrongNameKey = strongNameKey
        };

        if (context.Settings.Metadata.RemoveDebugInfo)
        {
            writerOptions.WritePdb = false;
        }

        var antiTamper = context.Settings.Protection.AntiTamper.Enabled;
        if (antiTamper && context.AntiTamperMetadata is null)
        {
            throw new InvalidOperationException("Anti-tamper is enabled but the runtime type was not injected.");
        }

        var writePath = antiTamper
            ? Path.Combine(Path.GetTempPath(), $"obfy_{Guid.NewGuid():N}.dll")
            : outputPath;

        try
        {
            // Control-flow (and other IL rewrites) can leave br.s / brfalse.s whose
            // targets no longer fit in a signed byte. dnlib then throws on write.
            foreach (var type in context.Module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    method.Body.SimplifyBranches();
                    method.Body.OptimizeBranches();
                }
            }

            context.Module.Write(writePath, writerOptions);
            foreach (var processor in _postProcessors)
                processor.Process(writePath, context);
            if (strongNameKey is not null)
                AssemblySigner.SignInPlace(writePath, strongNameKey);

            if (antiTamper)
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                File.Move(writePath, outputPath);
            }
        }
        finally
        {
            if (antiTamper && File.Exists(writePath))
            {
                try
                {
                    File.Delete(writePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp assembly {Path}", writePath);
                }
            }
        }

        if (context.Module is IDisposable disposable)
        {
            disposable.Dispose();
            context.Module = null;
        }

        _logger.LogDebug("Assembly saved successfully");

        return Task.CompletedTask;
    }
}
