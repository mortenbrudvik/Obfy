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

    public AssemblyProcessor(ILogger<AssemblyProcessor> logger)
    {
        _logger = logger;
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

        // Configure writer options
        var writerOptions = new ModuleWriterOptions(context.Module)
        {
            // Preserve metadata tokens for better compatibility
            MetadataOptions = { Flags = MetadataFlags.PreserveAll }
        };

        // Remove debug info if configured
        if (context.Settings.Metadata.RemoveDebugInfo)
        {
            writerOptions.WritePdb = false;
        }

        if (context.Settings.Protection.AntiTamper.Enabled)
        {
            if (context.AntiTamperMetadata is null)
            {
                throw new InvalidOperationException("Anti-tamper is enabled but the runtime type was not injected.");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"obfy_{Guid.NewGuid():N}.dll");

            try
            {
                context.Module.Write(tempPath, writerOptions);
                if (context.MethodEncryptionMetadata is not null)
                    MethodBodyPeEncryptor.Encrypt(tempPath, context.MethodEncryptionMetadata);
                AssemblyHashComputer.PatchIntegrityHash(tempPath);

                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(tempPath, outputPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temp assembly {Path}", tempPath);
                    }
                }
            }
        }
        else
        {
            context.Module.Write(outputPath, writerOptions);
            if (context.MethodEncryptionMetadata is not null)
                MethodBodyPeEncryptor.Encrypt(outputPath, context.MethodEncryptionMetadata);
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
