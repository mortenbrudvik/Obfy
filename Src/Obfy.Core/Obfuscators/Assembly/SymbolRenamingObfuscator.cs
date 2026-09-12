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

            ObfuscationAttributeRules.WarnIfInclusionsMatchedNothing(
                module, context.Settings, context.Warnings);

            var xamlBindableSeen = 0;

            // Collect all renamable symbols first
            var typeRenames = new Dictionary<TypeDef, string>();
            var methodRenames = new Dictionary<MethodDef, string>();
            var fieldRenames = new Dictionary<FieldDef, string>();
            var propertyRenames = new Dictionary<PropertyDef, string>();
            var eventRenames = new Dictionary<EventDef, string>();
            var namespaceRenames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var type in module.GetTypes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkipType(type, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
                    continue;

                if (settings.PreserveXaml && ObfuscatorHelpers.LooksLikeXamlBindable(type))
                    xamlBindableSeen++;

                // Rename type
                if (settings.RenameTypes && CanRenameType(type, settings, context.Settings.Inclusions))
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
                        if (CanRenameMethod(method, settings, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
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
                        if (CanRenameField(field, settings, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
                        {
                            var newName = _nameGenerator.Generate(field.Name, settings.Mode);
                            fieldRenames[field] = newName;
                            context.SymbolMap[$"Field:{type.FullName}.{field.Name}"] = newName;
                        }
                    }
                }

                if (settings.RenameProperties)
                {
                    foreach (var property in type.Properties)
                    {
                        if (CanRenameProperty(property, settings, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
                        {
                            var newName = _nameGenerator.Generate(property.Name, settings.Mode);
                            propertyRenames[property] = newName;
                            context.SymbolMap[$"Property:{type.FullName}.{property.Name}"] = newName;
                            if (property.GetMethod != null)
                                methodRenames[property.GetMethod] = "get_" + newName;
                            if (property.SetMethod != null)
                                methodRenames[property.SetMethod] = "set_" + newName;
                        }
                    }
                }

                if (settings.RenameEvents)
                {
                    foreach (var evt in type.Events)
                    {
                        if (CanRenameEvent(evt, settings, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
                        {
                            var newName = _nameGenerator.Generate(evt.Name, settings.Mode);
                            eventRenames[evt] = newName;
                            context.SymbolMap[$"Event:{type.FullName}.{evt.Name}"] = newName;
                            if (evt.AddMethod != null)
                                methodRenames[evt.AddMethod] = "add_" + newName;
                            if (evt.RemoveMethod != null)
                                methodRenames[evt.RemoveMethod] = "remove_" + newName;
                        }
                    }
                }

                if (settings.RenameNamespaces &&
                    !string.IsNullOrEmpty(type.Namespace) &&
                    type.Namespace != "Obfy.Core.Models" &&
                    !(settings.PreservePublicApi && type.IsPublic))
                {
                    var originalNs = type.Namespace.String;
                    if (!namespaceRenames.ContainsKey(originalNs))
                    {
                        var newNs = _nameGenerator.Generate(originalNs, settings.Mode);
                        namespaceRenames[originalNs] = newNs;
                        context.SymbolMap[$"Namespace:{originalNs}"] = newNs;
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

            foreach (var (evt, newName) in eventRenames)
            {
                evt.Name = newName;
                stats.EventsRenamed++;
            }

            var preservedNamespaces = new HashSet<string>(StringComparer.Ordinal);
            if (settings.PreservePublicApi)
            {
                foreach (var type in module.GetTypes())
                {
                    if (type.IsPublic && !string.IsNullOrEmpty(type.Namespace))
                        preservedNamespaces.Add(type.Namespace.String);
                }
            }

            foreach (var (originalNs, newNs) in namespaceRenames)
            {
                if (preservedNamespaces.Contains(originalNs))
                    continue;

                foreach (var type in module.GetTypes())
                {
                    if (type.Namespace == originalNs)
                        type.Namespace = newNs;
                }

                stats.NamespacesRenamed++;
            }

            // Rename parameters if enabled
            if (settings.RenameParameters)
            {
                foreach (var type in module.GetTypes())
                {
                    // Respect the same type-level exclusions used for members above (runtime-injected
                    // types, Obfy models, excluded namespaces/types).
                    if (ShouldSkipType(type, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        // Parameter names follow CanRenameMethod: skipped for constructors, entry
                        // points, virtuals/overrides/interface impls, and public methods when
                        // PreservePublicApi is set. Named-argument / reflection callers of public APIs
                        // are only safe with PreservePublicApi = true (the default is false).
                        if (!CanRenameMethod(method, settings, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
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

            if (settings.PreserveXaml && xamlBindableSeen == 0)
            {
                context.Warnings.Add(
                    "preserveXaml is enabled but no ViewModel/View/DependencyObject types were detected. " +
                    "Public properties on XAML types may have been renamed. Name view-models *ViewModel / *View, " +
                    "or ensure WPF/WinUI assemblies are resolvable.");
            }

            _logger.LogInformation(
                "Renamed {Types} types, {Methods} methods, {Fields} fields, {Properties} properties, {Parameters} parameters, {Events} events, {Namespaces} namespaces",
                stats.TypesRenamed, stats.MethodsRenamed, stats.FieldsRenamed,
                stats.PropertiesRenamed, stats.ParametersRenamed, stats.EventsRenamed, stats.NamespacesRenamed);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Symbol renaming failed");
            return Task.FromResult(ObfuscationResult.Failed($"Symbol renaming failed: {ex.Message}", ex));
        }
    }

    private static bool ShouldSkipType(
        TypeDef type,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        if (type.Namespace == "Obfy.Core.Models")
            return true;

        if (type.IsGlobalModuleType)
            return true;

        if (exclusions.Namespaces.Any(n => MatchesPattern(type.Namespace, n)))
            return true;

        if (exclusions.Types.Any(t => MatchesPattern(type.Name, t)))
            return true;

        if (ObfuscatorHelpers.HasExcludedAttribute(type, exclusions))
            return true;

        if (ObfuscatorHelpers.IsComVisibleTrue(type))
            return true;

        if (ObfuscationAttributeRules.IsExcluded(type, ObfuscationFeature.Renaming, warnings) &&
            ObfuscationAttributeRules.ApplyToMembers(type, ObfuscationFeature.Renaming))
            return true;

        if (!ObfuscationAttributeRules.TypeMayContainInclusions(type, inclusions))
            return true;

        return false;
    }

    private bool CanRenameType(TypeDef type, SymbolRenamingSettings settings, InclusionRules inclusions)
    {
        if (type.Module.EntryPoint?.DeclaringType == type)
            return false;

        if (settings.PreservePublicApi && type.IsPublic)
            return false;

        if (type.IsRuntimeSpecialName || type.IsSpecialName)
            return false;

        if (ObfuscationAttributeRules.IsExcluded(type, ObfuscationFeature.Renaming))
            return false;

        if (inclusions.HasAny && !TypeMatchedInclusions(type, inclusions))
            return false;

        return true;
    }

    private bool CanRenameMethod(
        MethodDef method,
        SymbolRenamingSettings settings,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        if (method.IsConstructor || method.IsStaticConstructor)
            return false;

        if (method.DeclaringType.Module.EntryPoint == method)
            return false;

        if (settings.PreservePublicApi && method.IsPublic)
            return false;

        if (method.IsRuntimeSpecialName || method.IsSpecialName)
            return false;

        if (ObfuscatorHelpers.MethodMatchesExclusion(method, exclusions))
            return false;

        if (IsMemberProtected(method, exclusions, warnings))
            return false;

        if (!ObfuscationAttributeRules.MatchesInclusions(method.DeclaringType, method, inclusions))
            return false;

        if (method.HasOverrides)
            return false;

        if (ImplementsInterface(method))
            return false;

        if (method.IsVirtual &&
            !method.DeclaringType.IsSealed &&
            (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly))
        {
            return false;
        }

        return true;
    }

    private static bool ImplementsInterface(MethodDef method)
    {
        if (method.DeclaringType.Interfaces.Count == 0)
            return false;

        foreach (var iface in method.DeclaringType.Interfaces)
        {
            var resolved = iface.Interface.ResolveTypeDef();
            if (resolved == null)
                continue;
            if (resolved.Methods.Any(m => m.Name == method.Name && m.MethodSig.Equals(method.MethodSig)))
                return true;
        }

        return false;
    }

    private static bool IsMemberProtected(IMemberDef member, ExclusionRules exclusions, ICollection<string> warnings) =>
        ObfuscatorHelpers.HasExcludedAttribute(member, exclusions) ||
        ObfuscatorHelpers.IsComVisibleTrue(member) ||
        ObfuscationAttributeRules.IsExcluded(member, ObfuscationFeature.Renaming, warnings);

    private static bool TypeMatchedInclusions(TypeDef type, InclusionRules inclusions) =>
        ObfuscationAttributeRules.MatchesInclusions(type, null, inclusions);

    private static bool CanRenameEvent(
        EventDef evt,
        SymbolRenamingSettings settings,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        var isPublic = (evt.AddMethod?.IsPublic ?? false) || (evt.RemoveMethod?.IsPublic ?? false);
        if (settings.PreservePublicApi && isPublic)
            return false;

        if (evt.IsSpecialName || evt.IsRuntimeSpecialName)
            return false;

        if (IsMemberProtected(evt, exclusions, warnings))
            return false;

        if (inclusions.HasAny && !TypeMatchedInclusions(evt.DeclaringType, inclusions))
            return false;

        return true;
    }

    private bool CanRenameField(
        FieldDef field,
        SymbolRenamingSettings settings,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        if (settings.PreservePublicApi && field.IsPublic)
            return false;

        if (field.IsRuntimeSpecialName || field.IsSpecialName)
            return false;

        if (field.IsLiteral)
            return false;

        if (IsMemberProtected(field, exclusions, warnings))
            return false;

        if (inclusions.HasAny && !TypeMatchedInclusions(field.DeclaringType, inclusions))
            return false;

        return true;
    }

    private bool CanRenameProperty(
        PropertyDef property,
        SymbolRenamingSettings settings,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        var isPublic = (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);

        if (settings.PreservePublicApi && isPublic)
            return false;

        if (property.IsRuntimeSpecialName || property.IsSpecialName)
            return false;

        if (IsMemberProtected(property, exclusions, warnings))
            return false;

        if (inclusions.HasAny && !TypeMatchedInclusions(property.DeclaringType, inclusions))
            return false;

        if (settings.PreserveXaml && isPublic &&
            !(property.GetMethod?.IsStatic ?? false) &&
            ObfuscatorHelpers.LooksLikeXamlBindable(property.DeclaringType))
            return false;

        return true;
    }

    private static bool MatchesPattern(string value, string pattern) => WildcardMatcher.IsMatch(value, pattern);
}
