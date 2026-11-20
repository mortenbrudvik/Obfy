using System.Text;
using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

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
    public int Priority => (int)ObfuscationPhase.MetadataRemoval;

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
        var module = context.RequireModule();
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Metadata removal failed");
            return Task.FromResult(ObfuscationResult.Failed($"Metadata removal failed: {ex.Message}", ex));
        }
    }

    private int RemoveDebugInfo(ModuleDef module)
    {
        var count = 0;

        // Compiler-built assemblies store DebuggableAttribute on the assembly, not the module.
        count += RemoveDebuggableAttributes(module.CustomAttributes);
        if (module.Assembly != null)
            count += RemoveDebuggableAttributes(module.Assembly.CustomAttributes);

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
        // NOTE: CompilationRelaxationsAttribute (e.g. NoStringInterning) and
        // RuntimeCompatibilityAttribute (WrapNonExceptionThrows) are intentionally NOT removed.
        // Stripping them alters runtime throw-wrapping / string-interning behavior for little
        // obfuscation gain.
        var attributesToRemove = new HashSet<string>
        {
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

        // Remove from assembly
        count += RemoveAttributesFrom(module.CustomAttributes, attributesToRemove, exclusions);

        // Remove from types and members
        foreach (var type in module.GetTypes())
        {
            count += RemoveAttributesFrom(type.CustomAttributes, attributesToRemove, exclusions);

            foreach (var method in type.Methods)
            {
                count += RemoveAttributesFrom(method.CustomAttributes, attributesToRemove, exclusions);

                foreach (var param in method.Parameters)
                {
                    count += RemoveAttributesFrom(param.ParamDef?.CustomAttributes, attributesToRemove, exclusions);
                }
            }

            foreach (var field in type.Fields)
            {
                count += RemoveAttributesFrom(field.CustomAttributes, attributesToRemove, exclusions);
            }

            foreach (var property in type.Properties)
            {
                count += RemoveAttributesFrom(property.CustomAttributes, attributesToRemove, exclusions);
            }

            foreach (var evt in type.Events)
            {
                count += RemoveAttributesFrom(evt.CustomAttributes, attributesToRemove, exclusions);
            }
        }

        _logger.LogDebug("Removed attributes: {Count} items", count);
        return count;
    }

    private static int RemoveDebuggableAttributes(CustomAttributeCollection attributes)
    {
        var count = 0;
        for (var i = attributes.Count - 1; i >= 0; i--)
        {
            if (attributes[i].TypeFullName == "System.Diagnostics.DebuggableAttribute")
            {
                attributes.RemoveAt(i);
                count++;
            }
        }

        return count;
    }

    private int RemoveAttributesFrom(
        CustomAttributeCollection? attributes,
        HashSet<string> toRemove,
        ExclusionRules exclusions)
    {
        if (attributes == null)
            return 0;

        var count = 0;
        for (var i = attributes.Count - 1; i >= 0; i--)
        {
            var name = attributes[i].TypeFullName;
            if (!toRemove.Contains(name))
                continue;
            if (exclusions.Attributes.Any(pattern => ObfuscatorHelpers.MatchesAttribute(name, pattern)))
                continue;

            attributes.RemoveAt(i);
            count++;
        }

        return count;
    }

    private int StripDocumentation(ModuleDef module)
    {
        var count = 0;

        // Remove embedded XML documentation only. A resource whose name ends in .xml
        // (config.xml, data.xml) is kept unless its content is a compiler doc file.
        for (var i = module.Resources.Count - 1; i >= 0; i--)
        {
            var resource = module.Resources[i];
            if (IsXmlDocumentation(resource))
            {
                module.Resources.RemoveAt(i);
                count++;
            }
        }

        _logger.LogDebug("Stripped documentation: {Count} items", count);
        return count;
    }

    private static bool IsXmlDocumentation(Resource resource)
    {
        if (resource is not EmbeddedResource embedded)
            return false;
        if (!embedded.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        string text;
        try
        {
            var data = embedded.CreateReader().ToArray();
            var length = Math.Min(data.Length, 16 * 1024);
            text = Encoding.UTF8.GetString(data, 0, length);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return false;
        }

        return HasDocElement(text, "doc") && HasDocElement(text, "members");
    }

    private static bool HasDocElement(string text, string name)
    {
        var needle = "<" + name;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var after = index + needle.Length;
            if (after >= text.Length)
                return false;

            var next = text[after];
            if (next is '>' or ' ' or '\t' or '\r' or '\n' or '/')
                return true;

            index = after;
        }

        return false;
    }
}
