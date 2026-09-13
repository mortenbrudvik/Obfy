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

                if (ShouldSkipType(type, context, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
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
                    if (type.Namespace == originalNs && !ObfuscatorHelpers.IsPinnedAttributeType(type))
                        type.Namespace = newNs;
                }

                stats.NamespacesRenamed++;
            }

            // Rename parameters if enabled
            if (settings.RenameParameters)
            {
                foreach (var type in module.GetTypes())
                {
                    // Respect the same type-level skip as members (pinned attributes, helpers with
                    // Rename = false, exclusions/inclusions).
                    if (ShouldSkipType(type, context, context.Settings.Exclusions, context.Settings.Inclusions, context.Warnings))
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Symbol renaming failed");
            return Task.FromResult(ObfuscationResult.Failed($"Symbol renaming failed: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Renames symbols across a closed set with a single name-generator reset, then copies
    /// renamed def names (and type namespaces) onto in-set <see cref="TypeRef"/>,
    /// <see cref="MemberRef"/>, and <see cref="ExportedType"/> rows that resolved to those defs.
    /// MemberRefs used from generic instantiations must be the instances CIL/MethodSpec hold;
    /// see <see cref="EnumerateMemberRefs"/>.
    /// </summary>
    public void RenameClosedSet(
        IReadOnlyList<(ModuleDef Module, ObfySettings Settings)> modules,
        PipelineContext sharedContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting closed-set symbol renaming for {Count} modules", modules.Count);

        _nameGenerator.Reset();

        var namespaceRenames = new Dictionary<string, string>(StringComparer.Ordinal);
        var plans = new List<(ModuleDef Module, ObfySettings Settings, ClosedSetRenamePlan Plan)>(modules.Count);
        var stats = new ObfuscationStatistics();

        foreach (var (module, settings) in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObfuscationAttributeRules.WarnIfInclusionsMatchedNothing(
                module, settings, sharedContext.Warnings);

            var plan = new ClosedSetRenamePlan();
            CollectClosedSetRenames(module, settings, sharedContext, plan, namespaceRenames, cancellationToken);
            plans.Add((module, settings, plan));
        }

        // Resolve while original names still match; apply mutates defs that the resolver keys on.
        // Match in-set refs by assembly name + TFM path so two TFMs of the same identity
        // do not steal each other's TypeRefs via the shared AssemblyResolver cache.
        var typeRefUpdates = new List<(TypeRef Ref, TypeDef Def)>();
        var memberRefUpdates = new List<(MemberRef Ref, IMemberDef Def)>();
        var exportedUpdates = new List<(ExportedType Ref, TypeDef Def)>();
        var set = plans.ConvertAll(static p => p.Module);
        foreach (var (module, _, _) in plans)
            SnapshotClosedSetReferences(
                module, set, typeRefUpdates, memberRefUpdates, exportedUpdates, sharedContext.Warnings);

        foreach (var (module, settings, plan) in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyClosedSetRenames(module, settings.SymbolRenaming, plan, namespaceRenames, stats);

            if (settings.SymbolRenaming.RenameParameters)
                RenameClosedSetParameters(module, settings, sharedContext, stats, cancellationToken);

            if (settings.SymbolRenaming.PreserveXaml && plan.XamlBindableSeen == 0)
            {
                sharedContext.Warnings.Add(
                    "preserveXaml is enabled but no ViewModel/View/DependencyObject types were detected. " +
                    "Public properties on XAML types may have been renamed. Name view-models *ViewModel / *View, " +
                    "or ensure WPF/WinUI assemblies are resolvable.");
            }
        }

        ApplyClosedSetReferenceUpdates(typeRefUpdates, memberRefUpdates, exportedUpdates);
        sharedContext.Statistics.Merge(stats);

        _logger.LogInformation(
            "Closed-set renamed {Types} types, {Methods} methods, {Fields} fields, {Properties} properties, {Parameters} parameters, {Events} events, {Namespaces} namespaces",
            stats.TypesRenamed, stats.MethodsRenamed, stats.FieldsRenamed,
            stats.PropertiesRenamed, stats.ParametersRenamed, stats.EventsRenamed, stats.NamespacesRenamed);
    }

    private void CollectClosedSetRenames(
        ModuleDef module,
        ObfySettings moduleSettings,
        PipelineContext sharedContext,
        ClosedSetRenamePlan plan,
        Dictionary<string, string> namespaceRenames,
        CancellationToken cancellationToken)
    {
        var settings = moduleSettings.SymbolRenaming;

        foreach (var type in module.GetTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipType(type, sharedContext, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                continue;

            if (settings.PreserveXaml && ObfuscatorHelpers.LooksLikeXamlBindable(type))
                plan.XamlBindableSeen++;

            if (settings.RenameTypes && CanRenameType(type, settings, moduleSettings.Inclusions))
            {
                var newName = _nameGenerator.Generate(type.Name, settings.Mode);
                plan.TypeRenames[type] = newName;
                sharedContext.SymbolMap[$"Type:{type.FullName}"] = newName;
            }

            if (settings.RenameMethods)
            {
                foreach (var method in type.Methods)
                {
                    if (CanRenameMethod(method, settings, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                    {
                        var newName = _nameGenerator.Generate(method.Name, settings.Mode);
                        plan.MethodRenames[method] = newName;
                        sharedContext.SymbolMap[$"Method:{type.FullName}.{method.Name}"] = newName;
                    }
                }
            }

            if (settings.RenameFields)
            {
                foreach (var field in type.Fields)
                {
                    if (CanRenameField(field, settings, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                    {
                        var newName = _nameGenerator.Generate(field.Name, settings.Mode);
                        plan.FieldRenames[field] = newName;
                        sharedContext.SymbolMap[$"Field:{type.FullName}.{field.Name}"] = newName;
                    }
                }
            }

            if (settings.RenameProperties)
            {
                foreach (var property in type.Properties)
                {
                    if (CanRenameProperty(property, settings, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                    {
                        var newName = _nameGenerator.Generate(property.Name, settings.Mode);
                        plan.PropertyRenames[property] = newName;
                        sharedContext.SymbolMap[$"Property:{type.FullName}.{property.Name}"] = newName;
                        if (property.GetMethod != null)
                            plan.MethodRenames[property.GetMethod] = "get_" + newName;
                        if (property.SetMethod != null)
                            plan.MethodRenames[property.SetMethod] = "set_" + newName;
                    }
                }
            }

            if (settings.RenameEvents)
            {
                foreach (var evt in type.Events)
                {
                    if (CanRenameEvent(evt, settings, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                    {
                        var newName = _nameGenerator.Generate(evt.Name, settings.Mode);
                        plan.EventRenames[evt] = newName;
                        sharedContext.SymbolMap[$"Event:{type.FullName}.{evt.Name}"] = newName;
                        if (evt.AddMethod != null)
                            plan.MethodRenames[evt.AddMethod] = "add_" + newName;
                        if (evt.RemoveMethod != null)
                            plan.MethodRenames[evt.RemoveMethod] = "remove_" + newName;
                    }
                }
            }

            if (settings.RenameNamespaces &&
                !string.IsNullOrEmpty(type.Namespace) &&
                !(settings.PreservePublicApi && type.IsPublic))
            {
                var originalNs = type.Namespace.String;
                if (!namespaceRenames.ContainsKey(originalNs))
                {
                    var newNs = _nameGenerator.Generate(originalNs, settings.Mode);
                    namespaceRenames[originalNs] = newNs;
                    sharedContext.SymbolMap[$"Namespace:{originalNs}"] = newNs;
                }

                plan.NamespacesToApply.Add(originalNs);
            }
        }
    }

    private static void ApplyClosedSetRenames(
        ModuleDef module,
        SymbolRenamingSettings settings,
        ClosedSetRenamePlan plan,
        Dictionary<string, string> namespaceRenames,
        ObfuscationStatistics stats)
    {
        foreach (var (type, newName) in plan.TypeRenames)
        {
            type.Name = newName;
            stats.TypesRenamed++;
        }

        foreach (var (method, newName) in plan.MethodRenames)
        {
            method.Name = newName;
            stats.MethodsRenamed++;
        }

        foreach (var (field, newName) in plan.FieldRenames)
        {
            field.Name = newName;
            stats.FieldsRenamed++;
        }

        foreach (var (property, newName) in plan.PropertyRenames)
        {
            property.Name = newName;
            stats.PropertiesRenamed++;
        }

        foreach (var (evt, newName) in plan.EventRenames)
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

        foreach (var originalNs in plan.NamespacesToApply)
        {
            if (preservedNamespaces.Contains(originalNs))
                continue;

            var newNs = namespaceRenames[originalNs];
            foreach (var type in module.GetTypes())
            {
                if (type.Namespace == originalNs && !ObfuscatorHelpers.IsPinnedAttributeType(type))
                    type.Namespace = newNs;
            }

            stats.NamespacesRenamed++;
        }
    }

    private void RenameClosedSetParameters(
        ModuleDef module,
        ObfySettings moduleSettings,
        PipelineContext sharedContext,
        ObfuscationStatistics stats,
        CancellationToken cancellationToken)
    {
        var settings = moduleSettings.SymbolRenaming;

        foreach (var type in module.GetTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipType(type, sharedContext, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
                continue;

            foreach (var method in type.Methods)
            {
                if (!CanRenameMethod(method, settings, moduleSettings.Exclusions, moduleSettings.Inclusions, sharedContext.Warnings))
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

    private static void SnapshotClosedSetReferences(
        ModuleDef module,
        IReadOnlyList<ModuleDef> set,
        List<(TypeRef Ref, TypeDef Def)> typeRefUpdates,
        List<(MemberRef Ref, IMemberDef Def)> memberRefUpdates,
        List<(ExportedType Ref, TypeDef Def)> exportedUpdates,
        ICollection<string> warnings)
    {
        foreach (var typeRef in module.GetTypeRefs())
        {
            var def = FindTypeDefInSet(typeRef, module, set);
            if (def != null)
                typeRefUpdates.Add((typeRef, def));
        }

        var seenMemberRefs = new HashSet<MemberRef>(ReferenceEqualityComparer.Instance);
        var unresolvedInSet = 0;
        foreach (var memberRef in EnumerateMemberRefs(module))
        {
            if (!seenMemberRefs.Add(memberRef))
                continue;

            var def = FindMemberDefInSet(memberRef, module, set);
            if (def != null)
                memberRefUpdates.Add((memberRef, def));
            else if (IsInSetMemberRef(memberRef, set))
                unresolvedInSet++;
        }

        if (unresolvedInSet > 0)
        {
            warnings.Add(
                $"{unresolvedInSet} MemberRef(s) in {module.Name} resolve to in-set assemblies but were not rewritten.");
        }

        foreach (var exported in module.ExportedTypes)
        {
            var def = FindExportedTypeInSet(exported, module, set);
            if (def != null)
                exportedUpdates.Add((exported, def));
        }
    }

    private static TypeDef? FindTypeDefInSet(TypeRef typeRef, ModuleDef referring, IReadOnlyList<ModuleDef> set)
    {
        var chosen = ChooseInSetModule(typeRef.DefinitionAssembly, referring, set);
        if (chosen is null)
            return typeRef.ResolveTypeDef();

        return FindType(chosen, typeRef.Namespace, typeRef.Name);
    }

    private static bool IsInSetMemberRef(MemberRef memberRef, IReadOnlyList<ModuleDef> set)
    {
        var declaring = memberRef.DeclaringType ?? memberRef.Class as ITypeDefOrRef;
        var name = declaring?.DefinitionAssembly?.Name?.String;
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var module in set)
        {
            if (module.Assembly?.Name?.String is { } moduleName
                && moduleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IMemberDef? FindMemberDefInSet(MemberRef memberRef, ModuleDef referring, IReadOnlyList<ModuleDef> set)
    {
        TypeDef? typeDef = memberRef.Class switch
        {
            TypeRef typeRef => FindTypeDefInSet(typeRef, referring, set),
            TypeDef declared => declared,
            TypeSpec spec => spec.ScopeType switch
            {
                TypeRef typeRef => FindTypeDefInSet(typeRef, referring, set),
                TypeDef declared => declared,
                _ => spec.ResolveTypeDef()
            },
            _ => null
        };

        if (typeDef is null)
            return memberRef.Resolve() as IMemberDef;

        if (memberRef.IsFieldRef)
            return typeDef.FindField(memberRef.Name, memberRef.FieldSig);

        return typeDef.FindMethod(memberRef.Name, memberRef.MethodSig);
    }

    private static TypeDef? FindExportedTypeInSet(ExportedType exported, ModuleDef referring, IReadOnlyList<ModuleDef> set)
    {
        var chosen = ChooseInSetModule(exported.DefinitionAssembly, referring, set);
        if (chosen is null)
            return exported.Resolve();

        return FindType(chosen, exported.TypeNamespace, exported.TypeName);
    }

    private static ModuleDef? ChooseInSetModule(IAssembly? assembly, ModuleDef referring, IReadOnlyList<ModuleDef> set)
    {
        var name = assembly?.Name?.String;
        if (string.IsNullOrEmpty(name))
            return referring;

        List<ModuleDef>? candidates = null;
        foreach (var module in set)
        {
            if (module.Assembly?.Name?.String is not { } moduleName)
                continue;
            if (!moduleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            candidates ??= new List<ModuleDef>();
            candidates.Add(module);
        }

        if (candidates is null || candidates.Count == 0)
            return null;
        if (candidates.Count == 1)
            return candidates[0];

        var tfm = ClosedSetPath.FindTfmSegment(referring.Location);
        foreach (var candidate in candidates)
        {
            if (string.Equals(ClosedSetPath.FindTfmSegment(candidate.Location), tfm, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return candidates[0];
    }

    private static TypeDef? FindType(ModuleDef module, UTF8String? ns, UTF8String? name)
    {
        foreach (var type in module.GetTypes())
        {
            if (type.Name == name && type.Namespace == ns)
                return type;
        }

        return null;
    }

    /// <summary>
    /// Yields the MemberRef instances the writer will emit.
    /// dnlib does not cache MemberRefs whose signatures contain generic parameters
    /// (<see cref="ModuleDef.GetMemberRefs"/> and <see cref="ModuleDefMD.ResolveMemberRef(uint)"/>
    /// both return a new copy). Mutating that copy leaves CIL / MethodSpec operands unchanged.
    /// Walk table RIDs for cached non-generic rows, then CIL operands and MethodSpec.Method
    /// for the live generic instances. Non-<see cref="ModuleDefMD"/> modules fall back to
    /// <see cref="ModuleDef.GetMemberRefs"/> plus the same CIL walk.
    /// </summary>
    private static IEnumerable<MemberRef> EnumerateMemberRefs(ModuleDef module)
    {
        if (module is ModuleDefMD md)
        {
            var rows = md.TablesStream.MemberRefTable.Rows;
            for (uint rid = 1; rid <= rows; rid++)
            {
                var memberRef = md.ResolveMemberRef(rid);
                if (memberRef != null)
                    yield return memberRef;
            }

            var methodSpecRows = md.TablesStream.MethodSpecTable.Rows;
            for (uint rid = 1; rid <= methodSpecRows; rid++)
            {
                var spec = md.ResolveMethodSpec(rid);
                if (spec?.Method is MemberRef fromSpec)
                    yield return fromSpec;
            }
        }
        else
        {
            foreach (var memberRef in module.GetMemberRefs())
                yield return memberRef;
        }

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body is null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MemberRef memberRef:
                            yield return memberRef;
                            break;
                        case MethodSpec { Method: MemberRef fromSpec }:
                            yield return fromSpec;
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Identity comparer: dnlib copies of generic MemberRefs are different objects;
    /// value equality would drop the instance the writer emits.
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<MemberRef>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(MemberRef? x, MemberRef? y) => ReferenceEquals(x, y);

        public int GetHashCode(MemberRef? obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static void ApplyClosedSetReferenceUpdates(
        List<(TypeRef Ref, TypeDef Def)> typeRefUpdates,
        List<(MemberRef Ref, IMemberDef Def)> memberRefUpdates,
        List<(ExportedType Ref, TypeDef Def)> exportedUpdates)
    {
        foreach (var (typeRef, def) in typeRefUpdates)
        {
            if (typeRef.Name != def.Name)
                typeRef.Name = def.Name;
            if (typeRef.Namespace != def.Namespace)
                typeRef.Namespace = def.Namespace;
        }

        foreach (var (memberRef, def) in memberRefUpdates)
        {
            if (memberRef.Name != def.Name)
                memberRef.Name = def.Name;
        }

        foreach (var (exported, def) in exportedUpdates)
        {
            if (exported.TypeName != def.Name)
                exported.TypeName = def.Name;
            if (exported.TypeNamespace != def.Namespace)
                exported.TypeNamespace = def.Namespace;
        }
    }

    private sealed class ClosedSetRenamePlan
    {
        public Dictionary<TypeDef, string> TypeRenames { get; } = new();
        public Dictionary<MethodDef, string> MethodRenames { get; } = new();
        public Dictionary<FieldDef, string> FieldRenames { get; } = new();
        public Dictionary<PropertyDef, string> PropertyRenames { get; } = new();
        public Dictionary<EventDef, string> EventRenames { get; } = new();
        public HashSet<string> NamespacesToApply { get; } = new(StringComparer.Ordinal);
        public int XamlBindableSeen { get; set; }
    }

    private static bool ShouldSkipType(
        TypeDef type,
        PipelineContext context,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        if (!RuntimeInjection.ShouldRename(context, type))
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
