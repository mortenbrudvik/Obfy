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
    public Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading assembly from {Path}", path);

        var moduleContext = ModuleDef.CreateModuleContext();
        var module = ModuleDefMD.Load(path, moduleContext);

        _logger.LogDebug("Loaded assembly {Name} with {TypeCount} types",
            module.Name, module.Types.Count);

        var context = PipelineContext.ForAssembly(module, settings);
        context.InputPath = path;

        return Task.FromResult(context);
    }

    /// <inheritdoc/>
    public Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default)
    {
        if (context.Module == null)
        {
            throw new InvalidOperationException("No module in context");
        }

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

        // Check if anti-tamper post-processing is needed
        if (context.Settings.Protection.AntiTamper.Enabled &&
            context.SharedData.TryGetValue(AntiTamperObfuscator.HashFieldMetadataKey, out var metadataObj) &&
            metadataObj is AntiTamperMetadata metadata)
        {
            // Write to temp file first
            var tempPath = Path.Combine(Path.GetTempPath(), $"obfy_{Guid.NewGuid():N}.dll");

            try
            {
                context.Module.Write(tempPath, writerOptions);
                _logger.LogDebug("Wrote assembly to temp file for anti-tamper processing");

                // Compute hash of the assembly (excluding the placeholder)
                var hash = AssemblyHashComputer.ComputeAssemblyHash(tempPath);
                _logger.LogDebug("Computed assembly hash: {Hash}", Convert.ToHexString(hash));

                // Patch the hash into the assembly
                AssemblyHashComputer.PatchHashFieldByToken(tempPath, metadata.HashFieldToken, hash);
                _logger.LogDebug("Patched anti-tamper hash into assembly");

                // Move to final output path
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(tempPath, outputPath);
            }
            finally
            {
                // Clean up temp file if it still exists
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }
        else
        {
            // Standard save without anti-tamper processing
            context.Module.Write(outputPath, writerOptions);
        }

        _logger.LogDebug("Assembly saved successfully");

        return Task.CompletedTask;
    }
}
