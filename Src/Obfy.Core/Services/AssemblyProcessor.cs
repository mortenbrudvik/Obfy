using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

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

        context.Module.Write(outputPath, writerOptions);

        _logger.LogDebug("Assembly saved successfully");

        return Task.CompletedTask;
    }
}
