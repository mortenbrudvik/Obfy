using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Renames types, methods, fields, and properties to obfuscate symbol names.
/// </summary>
public class SymbolRenamingObfuscator : IObfuscator
{
    private readonly INameGenerator _nameGenerator;
    private readonly ILogger<SymbolRenamingObfuscator> _logger;

    public SymbolRenamingObfuscator(INameGenerator nameGenerator, ILogger<SymbolRenamingObfuscator> logger)
    {
        _nameGenerator = nameGenerator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "SymbolRenaming";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.SymbolRenaming;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.SymbolRenaming.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.SymbolRenaming;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting symbol renaming with mode {Mode}", settings.Mode);

        try
        {
            _nameGenerator.Reset();

            // Collect all renamable symbols first
            var typeRenames = new Dictionary<TypeDef, string>();
            var methodRenames = new Dictionary<MethodDef, string>();
            var fieldRenames = new Dictionary<FieldDef, string>();
            var propertyRenames = new Dictionary<PropertyDef, string>();

            foreach (var type in module.GetTypes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkipType(type, settings, context.Settings.Exclusions))
                    continue;

                // Rename type
                if (settings.RenameTypes && CanRenameType(type, settings))
                {
                    var newName = _nameGenerator.Generate(type.Name, settings.Mode);
                    typeRenames[type] = newName;
                    context.SymbolMap[$"Type:{type.FullName}"] = newName;
                }

                // Rename methods
                if (settings.RenameMethods)
                {
                    foreach (var method in type.Methods)
                    {
                        if (CanRenameMethod(method, settings))
                        {
                            var newName = _nameGenerator.Generate(method.Name, settings.Mode);
                            methodRenames[method] = newName;
                            context.SymbolMap[$"Method:{type.FullName}.{method.Name}"] = newName;
                        }
                    }
                }

                // Rename fields
                if (settings.RenameFields)
                {
                    foreach (var field in type.Fields)
                    {
                        if (CanRenameField(field, settings))
                        {
                            var newName = _nameGenerator.Generate(field.Name, settings.Mode);
                            fieldRenames[field] = newName;
                            context.SymbolMap[$"Field:{type.FullName}.{field.Name}"] = newName;
                        }
                    }
                }

                // Rename properties
                if (settings.RenameProperties)
                {
                    foreach (var property in type.Properties)
                    {
                        if (CanRenameProperty(property, settings))
                        {
                            var newName = _nameGenerator.Generate(property.Name, settings.Mode);
                            propertyRenames[property] = newName;
                            context.SymbolMap[$"Property:{type.FullName}.{property.Name}"] = newName;
                        }
                    }
                }
            }

            // Apply renames
            foreach (var (type, newName) in typeRenames)
            {
                type.Name = newName;
                stats.TypesRenamed++;
            }

            foreach (var (method, newName) in methodRenames)
            {
                method.Name = newName;
                stats.MethodsRenamed++;
            }

            foreach (var (field, newName) in fieldRenames)
            {
                field.Name = newName;
                stats.FieldsRenamed++;
            }

            foreach (var (property, newName) in propertyRenames)
            {
                property.Name = newName;
                stats.PropertiesRenamed++;
            }

            // Rename parameters if enabled
            if (settings.RenameParameters)
            {
                foreach (var type in module.GetTypes())
                {
                    // Respect the same type-level exclusions used for members above (runtime-injected
                    // types, Obfy models, excluded namespaces/types).
                    if (ShouldSkipType(type, settings, context.Settings.Exclusions))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        // Only rename parameters of methods we would rename anyway. This honors
                        // PreservePublicApi, overrides, interface implementations and runtime methods,
                        // so we never rewrite parameter names on public APIs that callers bind by name
                        // (named arguments, reflection, model binding, DI-by-name).
                        if (!CanRenameMethod(method, settings))
                            continue;

                        foreach (var param in method.Parameters)
                        {
                            if (!string.IsNullOrEmpty(param.Name) && !param.IsHiddenThisParameter)
                            {
                                param.Name = _nameGenerator.Generate(param.Name, settings.Mode);
                                stats.ParametersRenamed++;
                            }
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Renamed {Types} types, {Methods} methods, {Fields} fields, {Properties} properties, {Parameters} parameters",
                stats.TypesRenamed, stats.MethodsRenamed, stats.FieldsRenamed,
                stats.PropertiesRenamed, stats.ParametersRenamed);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Symbol renaming failed");
            return Task.FromResult(ObfuscationResult.Failed($"Symbol renaming failed: {ex.Message}", ex));
        }
    }

    private bool ShouldSkipType(TypeDef type, SymbolRenamingSettings settings, ExclusionRules exclusions)
    {
        // Skip runtime-injected types
        if (type.Namespace == "Obfy.Runtime")
            return true;

        // Skip Obfy's own model types (required for JSON serialization)
        if (type.Namespace == "Obfy.Core.Models")
            return true;

        // Skip module type
        if (type.IsGlobalModuleType)
            return true;

        // Check exclusion rules
        if (exclusions.Namespaces.Any(n => MatchesPattern(type.Namespace, n)))
            return true;

        if (exclusions.Types.Any(t => MatchesPattern(type.Name, t)))
            return true;

        if (ObfuscatorHelpers.HasExcludedAttribute(type, exclusions))
            return true;

        return false;
    }

    private bool CanRenameType(TypeDef type, SymbolRenamingSettings settings)
    {
        // Don't rename entry point types
        if (type.Module.EntryPoint?.DeclaringType == type)
            return false;

        // Preserve public API if configured
        if (settings.PreservePublicApi && type.IsPublic)
            return false;

        // Don't rename special types
        if (type.IsRuntimeSpecialName || type.IsSpecialName)
            return false;

        return true;
    }

    private bool CanRenameMethod(MethodDef method, SymbolRenamingSettings settings)
    {
        // Don't rename constructors
        if (method.IsConstructor || method.IsStaticConstructor)
            return false;

        // Don't rename entry points
        if (method.DeclaringType.Module.EntryPoint == method)
            return false;

        // Preserve public API if configured
        if (settings.PreservePublicApi && method.IsPublic)
            return false;

        // Don't rename special methods
        if (method.IsRuntimeSpecialName || method.IsSpecialName)
            return false;

        if (method.IsVirtual)
            return false;

        if (method.HasOverrides)
            return false;

        if (method.DeclaringType.Interfaces.Count > 0)
        {
            foreach (var iface in method.DeclaringType.Interfaces)
            {
                var resolved = iface.Interface.ResolveTypeDef();
                if (resolved == null)
                    continue;
                if (resolved.Methods.Any(m => m.Name == method.Name && m.MethodSig.Equals(method.MethodSig)))
                    return false;
            }
        }

        return true;
    }

    private bool CanRenameField(FieldDef field, SymbolRenamingSettings settings)
    {
        // Preserve public API if configured
        if (settings.PreservePublicApi && field.IsPublic)
            return false;

        // Don't rename special fields
        if (field.IsRuntimeSpecialName || field.IsSpecialName)
            return false;

        // Don't rename literal fields (constants)
        if (field.IsLiteral)
            return false;

        return true;
    }

    private bool CanRenameProperty(PropertyDef property, SymbolRenamingSettings settings)
    {
        // Check if getter/setter is public
        var isPublic = (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);

        if (settings.PreservePublicApi && isPublic)
            return false;

        // Don't rename special properties
        if (property.IsRuntimeSpecialName || property.IsSpecialName)
            return false;

        return true;
    }

    private static bool MatchesPattern(string value, string pattern) => WildcardMatcher.IsMatch(value, pattern);
}
