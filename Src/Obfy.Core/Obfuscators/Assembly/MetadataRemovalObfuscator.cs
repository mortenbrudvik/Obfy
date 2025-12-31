using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Removes debug information and unnecessary metadata from assemblies.
/// </summary>
public class MetadataRemovalObfuscator : IObfuscator
{
    private readonly ILogger<MetadataRemovalObfuscator> _logger;

    public MetadataRemovalObfuscator(ILogger<MetadataRemovalObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "MetadataRemoval";

    /// <inheritdoc/>
    public int Priority => 90;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) =>
        settings.Metadata.RemoveDebugInfo ||
        settings.Metadata.RemoveAttributes ||
        settings.Metadata.StripDocumentation;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.Metadata;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting metadata removal");

        try
        {
            if (settings.RemoveDebugInfo)
            {
                stats.MetadataItemsRemoved += RemoveDebugInfo(module);
            }

            if (settings.RemoveAttributes)
            {
                stats.MetadataItemsRemoved += RemoveAttributes(module, context.Settings.Exclusions);
            }

            if (settings.StripDocumentation)
            {
                stats.MetadataItemsRemoved += StripDocumentation(module);
            }

            _logger.LogInformation("Removed {Count} metadata items", stats.MetadataItemsRemoved);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata removal failed");
            return Task.FromResult(ObfuscationResult.Failed($"Metadata removal failed: {ex.Message}", ex));
        }
    }

    private int RemoveDebugInfo(ModuleDef module)
    {
        var count = 0;

        // Remove DebuggableAttribute
        var debuggableAttr = module.CustomAttributes
            .FirstOrDefault(a => a.TypeFullName == "System.Diagnostics.DebuggableAttribute");

        if (debuggableAttr != null)
        {
            module.CustomAttributes.Remove(debuggableAttr);
            count++;
        }

        // Remove debug symbols from methods
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body?.PdbMethod != null)
                {
                    method.Body.PdbMethod = null;
                    count++;
                }

                // Clear local variable names
                if (method.Body?.Variables != null)
                {
                    foreach (var local in method.Body.Variables)
                    {
                        if (!string.IsNullOrEmpty(local.Name))
                        {
                            local.Name = null;
                            count++;
                        }
                    }
                }
            }
        }

        _logger.LogDebug("Removed debug info: {Count} items", count);
        return count;
    }

    private int RemoveAttributes(ModuleDef module, ExclusionRules exclusions)
    {
        var count = 0;
        var attributesToRemove = new HashSet<string>
        {
            "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
            "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
            "System.Runtime.CompilerServices.NullableAttribute",
            "System.Runtime.CompilerServices.NullableContextAttribute",
            "System.Diagnostics.DebuggerNonUserCodeAttribute",
            "System.Diagnostics.DebuggerStepThroughAttribute",
            "System.Diagnostics.DebuggerHiddenAttribute",
            "System.Diagnostics.DebuggerDisplayAttribute",
            "System.Diagnostics.DebuggerBrowsableAttribute",
            "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"
        };

        // Remove excluded attributes from the removal set
        foreach (var attr in exclusions.Attributes)
        {
            attributesToRemove.Remove(attr);
        }

        // Remove from assembly
        count += RemoveAttributesFrom(module.CustomAttributes, attributesToRemove);

        // Remove from types and members
        foreach (var type in module.GetTypes())
        {
            count += RemoveAttributesFrom(type.CustomAttributes, attributesToRemove);

            foreach (var method in type.Methods)
            {
                count += RemoveAttributesFrom(method.CustomAttributes, attributesToRemove);

                foreach (var param in method.Parameters)
                {
                    count += RemoveAttributesFrom(param.ParamDef?.CustomAttributes, attributesToRemove);
                }
            }

            foreach (var field in type.Fields)
            {
                count += RemoveAttributesFrom(field.CustomAttributes, attributesToRemove);
            }

            foreach (var property in type.Properties)
            {
                count += RemoveAttributesFrom(property.CustomAttributes, attributesToRemove);
            }

            foreach (var evt in type.Events)
            {
                count += RemoveAttributesFrom(evt.CustomAttributes, attributesToRemove);
            }
        }

        _logger.LogDebug("Removed attributes: {Count} items", count);
        return count;
    }

    private int RemoveAttributesFrom(CustomAttributeCollection? attributes, HashSet<string> toRemove)
    {
        if (attributes == null)
            return 0;

        var count = 0;
        for (var i = attributes.Count - 1; i >= 0; i--)
        {
            if (toRemove.Contains(attributes[i].TypeFullName))
            {
                attributes.RemoveAt(i);
                count++;
            }
        }

        return count;
    }

    private int StripDocumentation(ModuleDef module)
    {
        var count = 0;

        // Remove XML documentation resources
        for (var i = module.Resources.Count - 1; i >= 0; i--)
        {
            var resource = module.Resources[i];
            if (resource.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                module.Resources.RemoveAt(i);
                count++;
            }
        }

        _logger.LogDebug("Stripped documentation: {Count} items", count);
        return count;
    }
}
