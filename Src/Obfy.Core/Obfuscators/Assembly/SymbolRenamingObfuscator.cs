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
                            context.SymbolMap[MethodSymbolKey(type, method)] = newName;
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

            var runtimeNames = SnapshotRuntimeNames(module);

            // A member of a generic instantiation of one of this module's own types (Box<int>.Item) is not
            // referenced through its definition but through a MemberRef the CLR binds by name and signature,
            // and a named argument of a custom attribute ([Info(Note = "x")]) is stored as the member's name,
            // so every such reference has to follow the rename. Resolve them while the original names still
            // match; the definitions are renamed next. The module is the whole set, so nothing is resolved
            // through the assembly resolver.
            var referenceUpdates = new ReferenceUpdates();
            if (typeRenames.Count + methodRenames.Count + fieldRenames.Count + propertyRenames.Count
                + eventRenames.Count + namespaceRenames.Count > 0)
            {
                SnapshotReferences(
                    module, new[] { module }, referenceUpdates, context.Warnings, resolveOutsideSet: false);
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
                SyncDefaultMember(property, newName);
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

            ApplyReferenceUpdates(referenceUpdates);

            RenameDerivedResources(module, runtimeNames);

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
            _logger.LogError("Symbol renaming failed ({ExceptionType})", ex.GetType().Name);
            return Task.FromResult(ObfuscationResult.Failed($"Symbol renaming failed: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Renames symbols across a closed set with a single name-generator reset, then copies
    /// renamed def names (and type namespaces) onto in-set <see cref="TypeRef"/>,
    /// <see cref="MemberRef"/>, and <see cref="ExportedType"/> rows that resolved to those defs.
    /// MemberRefs used from generic instantiations must be the instances CIL/MethodSpec hold;
    /// see <see cref="EnumerateMemberRefs"/>. A single module gets the same treatment for the
    /// references it holds to its own definitions (<see cref="ObfuscateAsync"/>).
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
        var referenceUpdates = new ReferenceUpdates();
        var set = plans.ConvertAll(static p => p.Module);
        foreach (var (module, _, _) in plans)
            SnapshotReferences(module, set, referenceUpdates, sharedContext.Warnings, resolveOutsideSet: true);

        foreach (var (module, settings, plan) in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtimeNames = SnapshotRuntimeNames(module);
            ApplyClosedSetRenames(module, settings.SymbolRenaming, plan, namespaceRenames, stats);
            RenameDerivedResources(module, runtimeNames);

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

        ApplyReferenceUpdates(referenceUpdates);
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
                        sharedContext.SymbolMap[MethodSymbolKey(type, method)] = newName;
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
            SyncDefaultMember(property, newName);
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

    /// <summary>
    /// Pairs every reference of <paramref name="module"/> that points at a definition of <paramref name="set"/>
    /// with that definition, by the names both still carry: type, member and exported-type references, and
    /// the names inside custom attribute blobs (<see cref="SnapshotAttributeBlobs"/>). Nothing is renamed here. With
    /// <paramref name="resolveOutsideSet"/> a reference that names no assembly of the set is also resolved
    /// through the assembly resolver (a type forwarded into the set); a single-module run has no reason to.
    /// </summary>
    private static void SnapshotReferences(
        ModuleDef module,
        IReadOnlyList<ModuleDef> set,
        ReferenceUpdates updates,
        ICollection<string> warnings,
        bool resolveOutsideSet)
    {
        foreach (var typeRef in module.GetTypeRefs())
        {
            var def = FindTypeDefInSet(typeRef, module, set, resolveOutsideSet);
            if (def != null)
                updates.TypeRefs.Add((typeRef, def));
        }

        var seenMemberRefs = new HashSet<MemberRef>(ReferenceEqualityComparer.Instance);
        var unresolvedInSet = 0;
        foreach (var memberRef in EnumerateMemberRefs(module))
        {
            if (!seenMemberRefs.Add(memberRef))
                continue;

            var def = FindMemberDefInSet(memberRef, module, set, resolveOutsideSet);
            if (def != null)
                updates.MemberRefs.Add((memberRef, def));
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
            var def = FindExportedTypeInSet(exported, module, set, resolveOutsideSet);
            if (def != null)
                updates.ExportedTypes.Add((exported, def));
        }

        SnapshotAttributeBlobs(module, set, updates, warnings, resolveOutsideSet);
    }

    /// <summary>
    /// A custom attribute blob holds two kinds of names. A named argument (<c>[Info(Note = "x")]</c>) is stored as
    /// the NAME of a field or property of the attribute type, which the CLR looks up when the attribute is read:
    /// every named argument whose attribute type is defined in the set is paired with the field or property it
    /// names, found on the attribute type or on a base type in the set (attributes of framework types resolve
    /// to nothing in the set and are left as they are). An enum-typed argument and a <c>typeof</c> argument are
    /// stored as the type's NAME too, and dnlib hands such a type over as a type reference of its own, which
    /// is not a row of the TypeRef table: those references are paired with their definitions like the rows are.
    /// </summary>
    private static void SnapshotAttributeBlobs(
        ModuleDef module,
        IReadOnlyList<ModuleDef> set,
        ReferenceUpdates updates,
        ICollection<string> warnings,
        bool resolveOutsideSet)
    {
        var unparsed = 0;
        foreach (var owner in CustomAttributeOwners(module).Concat(ReferenceCustomAttributeOwners(module)))
        {
            foreach (var attribute in owner.CustomAttributes)
            {
                if (attribute.IsRawBlob)
                {
                    // dnlib could not parse the blob (an enum it cannot resolve), so the names in it stay opaque.
                    if (ResolveTypeInSet(attribute.AttributeType, module, set, resolveOutsideSet) != null)
                        unparsed++;
                    continue;
                }

                AddNamedArguments(attribute.AttributeType, attribute.NamedArguments, module, set, updates, resolveOutsideSet);
                AddBlobTypeRefs(attribute.ConstructorArguments, attribute.NamedArguments, module, set, updates, resolveOutsideSet);
            }

            if (owner is DeclSecurity declSecurity)
            {
                foreach (var security in declSecurity.SecurityAttributes)
                {
                    AddNamedArguments(security.AttributeType, security.NamedArguments, module, set, updates, resolveOutsideSet);
                    AddBlobTypeRefs(Array.Empty<CAArgument>(), security.NamedArguments, module, set, updates, resolveOutsideSet);
                }
            }
        }

        if (unparsed > 0)
        {
            warnings.Add(
                $"{unparsed} custom attribute(s) in {module.Name} of an in-set type could not be parsed, " +
                "so the names inside them were not rewritten.");
        }
    }

    private static void AddBlobTypeRefs(
        IList<CAArgument> positional,
        IList<CANamedArgument> named,
        ModuleDef referring,
        IReadOnlyList<ModuleDef> set,
        ReferenceUpdates updates,
        bool resolveOutsideSet)
    {
        foreach (var typeRef in TypeRefsInArguments(positional, named))
        {
            var def = FindTypeDefInSet(typeRef, referring, set, resolveOutsideSet);
            if (def != null)
                updates.TypeRefs.Add((typeRef, def));
        }
    }

    private static IEnumerable<TypeRef> TypeRefsInArguments(IList<CAArgument> positional, IList<CANamedArgument> named)
    {
        foreach (var argument in positional)
        {
            foreach (var typeRef in TypeRefsInArgument(argument))
                yield return typeRef;
        }

        foreach (var argument in named)
        {
            foreach (var typeRef in TypeRefsInSig(argument.Type))
                yield return typeRef;
            foreach (var typeRef in TypeRefsInArgument(argument.Argument))
                yield return typeRef;
        }
    }

    private static IEnumerable<TypeRef> TypeRefsInArgument(CAArgument argument)
    {
        foreach (var typeRef in TypeRefsInSig(argument.Type))
            yield return typeRef;

        switch (argument.Value)
        {
            case TypeSig sig:
                foreach (var typeRef in TypeRefsInSig(sig))
                    yield return typeRef;
                break;
            case CAArgument boxed:
                foreach (var typeRef in TypeRefsInArgument(boxed))
                    yield return typeRef;
                break;
            case IList<CAArgument> elements:
                foreach (var element in elements)
                {
                    foreach (var typeRef in TypeRefsInArgument(element))
                        yield return typeRef;
                }

                break;
        }
    }

    private static IEnumerable<TypeRef> TypeRefsInSig(TypeSig? sig)
    {
        for (; sig != null; sig = sig.Next)
        {
            switch (sig)
            {
                case GenericInstSig generic:
                    foreach (var typeRef in TypeRefsInSig(generic.GenericType))
                        yield return typeRef;
                    foreach (var argument in generic.GenericArguments)
                    {
                        foreach (var typeRef in TypeRefsInSig(argument))
                            yield return typeRef;
                    }

                    break;
                case ClassOrValueTypeSig named:
                    foreach (var typeRef in TypeRefsInType(named.TypeDefOrRef))
                        yield return typeRef;
                    break;
                case ModifierSig modifier:
                    foreach (var typeRef in TypeRefsInType(modifier.Modifier))
                        yield return typeRef;
                    break;
            }
        }
    }

    /// <summary>A type reference and the enclosing type references a nested one names as its scope.</summary>
    private static IEnumerable<TypeRef> TypeRefsInType(ITypeDefOrRef? type)
    {
        switch (type)
        {
            case TypeRef typeRef:
                yield return typeRef;
                foreach (var enclosing in TypeRefsInType(typeRef.ResolutionScope as TypeRef))
                    yield return enclosing;
                break;
            case TypeSpec spec:
                foreach (var typeRef in TypeRefsInSig(spec.TypeSig))
                    yield return typeRef;
                break;
        }
    }

    private static void AddNamedArguments(
        ITypeDefOrRef? attributeType,
        IList<CANamedArgument> arguments,
        ModuleDef referring,
        IReadOnlyList<ModuleDef> set,
        ReferenceUpdates updates,
        bool resolveOutsideSet)
    {
        if (arguments.Count == 0)
            return;

        var attributeDef = ResolveTypeInSet(attributeType, referring, set, resolveOutsideSet);
        if (attributeDef is null)
            return;

        foreach (var argument in arguments)
        {
            if (UTF8String.IsNullOrEmpty(argument.Name))
                continue;

            var member = FindNamedArgumentMember(attributeDef, argument, set, resolveOutsideSet);
            if (member != null)
                updates.NamedArguments.Add((argument, member));
        }
    }

    /// <summary>
    /// The field (<see cref="CANamedArgument.IsField"/>) or property a named argument names: the CLR looks it up
    /// on the attribute type first and then up the base chain, so a member of a base attribute is found the same way.
    /// </summary>
    private static IMemberDef? FindNamedArgumentMember(
        TypeDef attributeDef, CANamedArgument argument, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet)
    {
        foreach (var (type, _) in TypeAndBases(attributeDef, set, resolveOutsideSet))
        {
            if (argument.IsField)
            {
                foreach (var field in type.Fields)
                {
                    if (field.Name == argument.Name)
                        return field;
                }
            }
            else
            {
                foreach (var property in type.Properties)
                {
                    if (property.Name == argument.Name)
                        return property;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="start"/> and its base types as far as they are definitions of the set (or, with
    /// <paramref name="resolveOutsideSet"/>, resolvable). A base given as a generic instantiation
    /// (<c>Base&lt;int&gt;</c>) is followed through its generic type, and each type comes with what its generic
    /// parameters stand for, written in terms of the generic parameters of <paramref name="start"/> (null: they stand
    /// for themselves, which is how a member reference to <paramref name="start"/> itself writes them).
    /// </summary>
    private static IEnumerable<(TypeDef Type, IList<TypeSig>? TypeArguments)> TypeAndBases(
        TypeDef start, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet)
    {
        var seen = new HashSet<TypeDef>();
        IList<TypeSig>? arguments = null;
        for (var type = start; type != null && seen.Add(type); )
        {
            yield return (type, arguments);

            var baseType = type.BaseType;
            var next = ResolveTypeInSet(baseType, type.Module, set, resolveOutsideSet);
            if (baseType is TypeSpec { TypeSig: GenericInstSig instantiation })
            {
                // The base's generic parameters stand for the instantiation's arguments, which may mention the
                // generic parameters of `type`, which in turn stand for `arguments`.
                arguments = arguments is null
                    ? instantiation.GenericArguments
                    : instantiation.GenericArguments.Select(a => Substitute(a, arguments)!).ToList();
            }
            else
            {
                arguments = null;
            }

            type = next;
        }
    }

    /// <summary>
    /// Replaces the generic parameters (<c>!0</c>) of <paramref name="sig"/> with <paramref name="typeArguments"/>: how
    /// the CLR reads a member of a base type, <c>T Get()</c> of <c>Box&lt;T&gt;</c>, when it compares it with a reference
    /// written for a derived type, <c>int32 ()</c> for <c>IntBox : Box&lt;int&gt;</c>. Method generic parameters
    /// (<c>!!0</c>) stay. (dnlib has the same helper, but not public.)
    /// </summary>
    private static TypeSig? Substitute(TypeSig? sig, IList<TypeSig> typeArguments)
    {
        switch (sig)
        {
            case null:
                return null;
            case GenericVar variable:
                return variable.Number < typeArguments.Count ? typeArguments[(int)variable.Number] : sig;
            case GenericInstSig generic:
                return new GenericInstSig(
                    generic.GenericType,
                    generic.GenericArguments.Select(argument => Substitute(argument, typeArguments)!).ToArray());
            case SZArraySig array:
                return new SZArraySig(Substitute(array.Next, typeArguments));
            case ArraySig array:
                return new ArraySig(Substitute(array.Next, typeArguments), array.Rank, array.Sizes, array.LowerBounds);
            case PtrSig pointer:
                return new PtrSig(Substitute(pointer.Next, typeArguments));
            case ByRefSig byRef:
                return new ByRefSig(Substitute(byRef.Next, typeArguments));
            case PinnedSig pinned:
                return new PinnedSig(Substitute(pinned.Next, typeArguments));
            case CModReqdSig required:
                return new CModReqdSig(required.Modifier, Substitute(required.Next, typeArguments));
            case CModOptSig optional:
                return new CModOptSig(optional.Modifier, Substitute(optional.Next, typeArguments));
            default:
                return sig;
        }
    }

    private static MethodSig Substitute(MethodSig sig, IList<TypeSig> typeArguments) =>
        new(
            sig.CallingConvention,
            sig.GenParamCount,
            Substitute(sig.RetType, typeArguments),
            sig.Params.Select(parameter => Substitute(parameter, typeArguments)!).ToList());

    /// <summary>
    /// The definition a type reference, a definition or an instantiation of a generic type
    /// (<c>Box&lt;int&gt;</c>) names, or null when it is none of the program's own (an array, a pointer, a
    /// type outside the set). <paramref name="referring"/> is the module that holds the reference.
    /// </summary>
    private static TypeDef? ResolveTypeInSet(
        ITypeDefOrRef? type, ModuleDef referring, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet) =>
        type switch
        {
            TypeDef def => def,
            TypeRef typeRef => FindTypeDefInSet(typeRef, referring, set, resolveOutsideSet),
            TypeSpec { TypeSig: GenericInstSig { GenericType: { } generic } } =>
                ResolveTypeInSet(generic.TypeDefOrRef, referring, set, resolveOutsideSet),
            TypeSpec { TypeSig: ClassOrValueTypeSig plain } =>
                ResolveTypeInSet(plain.TypeDefOrRef, referring, set, resolveOutsideSet),
            _ => null
        };

    private static TypeDef? FindTypeDefInSet(
        TypeRef typeRef, ModuleDef referring, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet)
    {
        var chosen = ChooseInSetModule(typeRef.DefinitionAssembly, referring, set);
        if (chosen is null)
            return resolveOutsideSet ? typeRef.ResolveTypeDef() : null;

        return FindType(chosen, typeRef);
    }

    /// <summary>
    /// The type a member reference names as its parent: the definition or reference itself, or the generic
    /// type of an instantiation (<c>Box&lt;int&gt;</c>, which is how C# refers to a member of a generic type).
    /// Null for the parents that are not a type of the program: arrays, pointers and by-ref types carry the
    /// runtime's own members (<c>Get</c>, <c>Set</c>, <c>Address</c>), never the element type's, so a reference
    /// to one must not be matched to a definition of the element type.
    /// </summary>
    private static ITypeDefOrRef? DeclaringTypeOf(MemberRef memberRef) =>
        memberRef.Class switch
        {
            TypeDef typeDef => typeDef,
            TypeRef typeRef => typeRef,
            TypeSpec { TypeSig: GenericInstSig { GenericType: { } generic } } => generic.TypeDefOrRef,
            TypeSpec { TypeSig: ClassOrValueTypeSig plain } => plain.TypeDefOrRef,
            _ => null
        };

    private static bool IsInSetMemberRef(MemberRef memberRef, IReadOnlyList<ModuleDef> set)
    {
        var name = DeclaringTypeOf(memberRef)?.DefinitionAssembly?.Name?.String;
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

    private static IMemberDef? FindMemberDefInSet(
        MemberRef memberRef, ModuleDef referring, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet)
    {
        // A vararg call site names its target through a MemberRef whose parent is the MethodDef itself.
        if (memberRef.Class is MethodDef vararg)
            return vararg;

        var declaring = DeclaringTypeOf(memberRef);
        if (declaring is null)
            return null;

        var typeDef = ResolveTypeInSet(declaring, referring, set, resolveOutsideSet);

        if (typeDef is null)
            return resolveOutsideSet ? memberRef.Resolve() as IMemberDef : null;

        // The member is on the type the reference names or, like the CLR looks for it, on a base type. A reference
        // written for a derived type (no compiler does it, but the metadata allows it) names the base type's member the
        // way the derived type sees it: with the arguments of the base's instantiation in place of its generic parameters.
        foreach (var (type, arguments) in TypeAndBases(typeDef, set, resolveOutsideSet))
        {
            IMemberDef? found = memberRef.IsFieldRef
                ? FindField(type, arguments, memberRef)
                : FindMethod(type, arguments, memberRef);
            if (found != null)
                return found;
        }

        return null;
    }

    private static FieldDef? FindField(TypeDef type, IList<TypeSig>? typeArguments, MemberRef memberRef)
    {
        if (typeArguments is null)
            return type.FindField(memberRef.Name, memberRef.FieldSig);

        var comparer = new SigComparer();
        foreach (var field in type.Fields)
        {
            if (field.Name == memberRef.Name
                && comparer.Equals(new FieldSig(Substitute(field.FieldSig.Type, typeArguments)), memberRef.FieldSig))
                return field;
        }

        return null;
    }

    private static MethodDef? FindMethod(TypeDef type, IList<TypeSig>? typeArguments, MemberRef memberRef)
    {
        if (typeArguments is null)
            return type.FindMethod(memberRef.Name, memberRef.MethodSig);

        // Substitution can make several methods share the reference's signature: Pick(T) and Pick(int) are
        // different methods on Base<T> and the same method shape on Base<int>. CoreCLR then prefers a virtual
        // match over a non-virtual one and, within that group, the last method in metadata order. Taking the
        // first match renames the reference to the other method, and the call runs that method instead.
        var comparer = new SigComparer();
        MethodDef? match = null;
        foreach (var method in type.Methods)
        {
            if (method.Name != memberRef.Name
                || !comparer.Equals(Substitute(method.MethodSig, typeArguments), memberRef.MethodSig))
                continue;

            if (match is null || PreferCollapsedMethod(method, match))
                match = method;
        }

        return match;
    }

    /// <summary>
    /// <paramref name="candidate"/> is the match CoreCLR would keep when both methods share a signature after
    /// substitution. A virtual method beats a non-virtual one. Otherwise the later method wins, because this is
    /// called as the type's methods are walked in metadata order.
    /// </summary>
    private static bool PreferCollapsedMethod(MethodDef candidate, MethodDef current)
    {
        if (candidate.IsVirtual != current.IsVirtual)
            return candidate.IsVirtual;

        return true;
    }

    private static TypeDef? FindExportedTypeInSet(
        ExportedType exported, ModuleDef referring, IReadOnlyList<ModuleDef> set, bool resolveOutsideSet)
    {
        var chosen = ChooseInSetModule(exported.DefinitionAssembly, referring, set);
        if (chosen is null)
            return resolveOutsideSet ? exported.Resolve() : null;

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

    private static TypeDef? FindType(ModuleDef module, TypeRef typeRef)
    {
        // Nested TypeRefs carry an empty namespace and the enclosing type as their
        // resolution scope. Matching only the simple name collapses two nested types
        // that share that name (Enumerator, Builder, ...).
        if (typeRef.ResolutionScope is TypeRef enclosingRef)
        {
            var enclosing = FindType(module, enclosingRef);
            if (enclosing is null)
                return null;

            foreach (var nested in enclosing.NestedTypes)
            {
                if (nested.Name == typeRef.Name)
                    return nested;
            }

            return null;
        }

        return FindType(module, typeRef.Namespace, typeRef.Name);
    }

    private static TypeDef? FindType(ModuleDef module, UTF8String? ns, UTF8String? name)
    {
        foreach (var type in module.Types)
        {
            if (type.Name == name && type.Namespace == ns)
                return type;
        }

        return null;
    }

    private static string MethodSymbolKey(TypeDef type, MethodDef method)
    {
        var signature = method.MethodSig?.ToString() ?? "";
        return $"Method:{type.FullName}.{method.Name}({signature})";
    }

    private static Dictionary<TypeDef, string> SnapshotRuntimeNames(ModuleDef module)
    {
        var names = new Dictionary<TypeDef, string>();
        foreach (var type in module.GetTypes())
            names[type] = RuntimeFullName(type);
        return names;
    }

    /// <summary>
    /// Runtime <see cref="Type.FullName"/> uses <c>+</c> for nested types. dnlib uses <c>/</c>.
    /// WinForms resource names are the runtime full name plus <c>.resources</c>.
    /// </summary>
    private static string RuntimeFullName(TypeDef type)
    {
        if (type.DeclaringType is { } parent)
            return RuntimeFullName(parent) + "+" + type.Name.String;

        var ns = type.Namespace?.String ?? "";
        return ns.Length == 0 ? type.Name.String : ns + "." + type.Name.String;
    }

    private static void RenameDerivedResources(ModuleDef module, Dictionary<TypeDef, string> oldRuntimeNames)
    {
        if (module.Resources.Count == 0 || oldRuntimeNames.Count == 0)
            return;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, oldName) in oldRuntimeNames)
        {
            var newName = RuntimeFullName(type);
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                continue;
            map[oldName + ".resources"] = newName + ".resources";
        }

        if (map.Count == 0)
            return;

        foreach (var resource in module.Resources)
        {
            if (map.TryGetValue(resource.Name, out var updated))
                resource.Name = updated;
        }
    }

    private static void SyncDefaultMember(PropertyDef property, string newName)
    {
        var type = property.DeclaringType;
        if (type is null || !type.HasCustomAttributes)
            return;

        var oldName = property.Name.String;
        foreach (var attr in type.CustomAttributes)
        {
            var attrName = attr.AttributeType?.Name?.String ?? "";
            if (attrName != "DefaultMemberAttribute" &&
                !attr.TypeFullName.EndsWith(".DefaultMemberAttribute", StringComparison.Ordinal))
                continue;
            if (attr.ConstructorArguments.Count == 0)
                continue;

            var current = attr.ConstructorArguments[0].Value switch
            {
                UTF8String utf => utf.String,
                string text => text,
                _ => null
            };
            if (!string.Equals(current, oldName, StringComparison.Ordinal))
                continue;

            var arg = attr.ConstructorArguments[0];
            attr.ConstructorArguments[0] = new CAArgument(arg.Type, new UTF8String(newName));
        }
    }

    /// <summary>
    /// Yields the MemberRef instances the writer will emit.
    /// dnlib does not cache MemberRefs whose signatures contain generic parameters
    /// (<see cref="ModuleDef.GetMemberRefs"/> and <see cref="ModuleDefMD.ResolveMemberRef(uint)"/>
    /// both return a new copy). Mutating that copy leaves CIL / MethodSpec operands unchanged.
    /// Walk table RIDs for cached non-generic rows, then every place a MemberRef is held by an object
    /// the writer reaches: CIL operands, <see cref="MethodSpec.Method"/>, custom attribute constructors
    /// and method overrides. Non-<see cref="ModuleDefMD"/> modules fall back to
    /// <see cref="ModuleDef.GetMemberRefs"/> plus the same walk.
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

        foreach (var owner in CustomAttributeOwners(module))
        {
            foreach (var attribute in owner.CustomAttributes)
            {
                if (attribute.Constructor is MemberRef constructor)
                    yield return constructor;
            }
        }

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                foreach (var methodOverride in method.Overrides)
                {
                    if (methodOverride.MethodDeclaration is MemberRef declaration)
                        yield return declaration;
                    if (methodOverride.MethodBody is MemberRef body)
                        yield return body;
                }

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
    /// Everything of <paramref name="module"/> that carries custom attributes and that the writer reaches:
    /// the module and assembly, the types with their interface implementations, generic parameters (and their
    /// constraints), fields, properties, events, methods, parameters and return values, declarative security,
    /// exported types, resources, and the assembly, module and type references.
    /// </summary>
    private static IEnumerable<IHasCustomAttribute> CustomAttributeOwners(ModuleDef module)
    {
        yield return module;
        if (module.Assembly is { } assembly)
        {
            yield return assembly;
            foreach (var security in assembly.DeclSecurities)
                yield return security;
        }

        foreach (var exported in module.ExportedTypes)
            yield return exported;
        foreach (var resource in module.Resources)
            yield return resource;
        foreach (var assemblyRef in module.GetAssemblyRefs())
            yield return assemblyRef;
        foreach (var moduleRef in module.GetModuleRefs())
            yield return moduleRef;
        foreach (var typeRef in module.GetTypeRefs())
            yield return typeRef;

        foreach (var type in module.GetTypes())
        {
            yield return type;
            foreach (var security in type.DeclSecurities)
                yield return security;
            foreach (var implementation in type.Interfaces)
                yield return implementation;
            foreach (var owner in GenericParameterOwners(type.GenericParameters))
                yield return owner;
            foreach (var field in type.Fields)
                yield return field;
            foreach (var property in type.Properties)
                yield return property;
            foreach (var evt in type.Events)
                yield return evt;

            foreach (var method in type.Methods)
            {
                yield return method;
                foreach (var security in method.DeclSecurities)
                    yield return security;
                foreach (var owner in GenericParameterOwners(method.GenericParameters))
                    yield return owner;
                foreach (var parameter in method.ParamDefs)
                    yield return parameter;
            }
        }
    }

    /// <summary>
    /// The member references, method specifications and type specifications the code and the type definitions hold,
    /// as the instances the writer emits (see <see cref="EnumerateMemberRefs"/>). They can carry attributes
    /// like any other row.
    /// </summary>
    private static IEnumerable<IHasCustomAttribute> ReferenceCustomAttributeOwners(ModuleDef module)
    {
        var seen = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        foreach (var memberRef in EnumerateMemberRefs(module))
        {
            if (seen.Add(memberRef))
                yield return memberRef;
            if (memberRef.Class is TypeSpec parent && seen.Add(parent))
                yield return parent;
        }

        foreach (var type in module.GetTypes())
        {
            if (type.BaseType is TypeSpec baseSpec && seen.Add(baseSpec))
                yield return baseSpec;
            foreach (var implementation in type.Interfaces)
            {
                if (implementation.Interface is TypeSpec interfaceSpec && seen.Add(interfaceSpec))
                    yield return interfaceSpec;
            }

            foreach (var method in type.Methods)
            {
                if (method.Body is null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case TypeSpec typeSpec when seen.Add(typeSpec):
                            yield return typeSpec;
                            break;
                        case MethodSpec methodSpec:
                            if (seen.Add(methodSpec))
                                yield return methodSpec;
                            if (methodSpec.Method is MemberRef { Class: TypeSpec genericParent } && seen.Add(genericParent))
                                yield return genericParent;
                            break;
                    }
                }
            }
        }
    }

    private static IEnumerable<IHasCustomAttribute> GenericParameterOwners(IList<GenericParam> parameters)
    {
        foreach (var parameter in parameters)
        {
            yield return parameter;
            foreach (var constraint in parameter.GenericParamConstraints)
                yield return constraint;
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

    /// <summary>What <see cref="SnapshotReferences"/> found, to be applied once the definitions carry their new names.</summary>
    private sealed class ReferenceUpdates
    {
        public List<(TypeRef Ref, TypeDef Def)> TypeRefs { get; } = new();
        public List<(MemberRef Ref, IMemberDef Def)> MemberRefs { get; } = new();
        public List<(ExportedType Ref, TypeDef Def)> ExportedTypes { get; } = new();
        public List<(CANamedArgument Argument, IMemberDef Def)> NamedArguments { get; } = new();
    }

    private static void ApplyReferenceUpdates(ReferenceUpdates updates)
    {
        foreach (var (typeRef, def) in updates.TypeRefs)
        {
            if (typeRef.Name != def.Name)
                typeRef.Name = def.Name;
            // Nested TypeRefs keep an empty namespace; the enclosing TypeRef owns the namespace.
            if (typeRef.ResolutionScope is TypeRef)
                continue;
            if (typeRef.Namespace != def.Namespace)
                typeRef.Namespace = def.Namespace;
        }

        foreach (var (memberRef, def) in updates.MemberRefs)
        {
            if (memberRef.Name != def.Name)
                memberRef.Name = def.Name;
        }

        foreach (var (exported, def) in updates.ExportedTypes)
        {
            if (exported.TypeName != def.Name)
                exported.TypeName = def.Name;
            if (exported.TypeNamespace != def.Namespace)
                exported.TypeNamespace = def.Namespace;
        }

        // A property is renamed together with its accessors; the argument names the PROPERTY, not get_/set_.
        foreach (var (argument, def) in updates.NamedArguments)
        {
            if (argument.Name != def.Name)
                argument.Name = def.Name;
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

        if (TakesPartInVirtualDispatch(method))
            return false;

        return true;
    }

    /// <summary>
    /// A virtual method is matched by name and signature against its base type's slot (or an
    /// interface method), and the base may live in another assembly, so renaming only one side
    /// turns an override into a new vtable slot and dispatch through the base reaches the wrong
    /// code (or the type fails to load). Structs and sealed classes are not exempt: their
    /// overrides (<c>ToString</c>, <c>Finalize</c>, ...), implicit interface implementations
    /// (including generic and inherited interfaces) and the runtime-implemented delegate
    /// <c>Invoke</c> all rely on the original name. <c>internal</c> and <c>private protected</c>
    /// virtuals are covered too: the override is renamed independently of its base otherwise.
    /// </summary>
    private static bool TakesPartInVirtualDispatch(MethodDef method) => method.IsVirtual;

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

    private bool CanRenameEvent(
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

        if (!AllAccessorsRenamable(EventAccessors(evt), settings, exclusions, inclusions, warnings))
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

        if (property.IsRuntimeSpecialName)
            return false;

        // Indexers are special-name, but the default-member string is their name.
        // Other special-name properties stay put.
        if (property.IsSpecialName && !IsIndexer(property))
            return false;

        if (IsMemberProtected(property, exclusions, warnings))
            return false;

        if (inclusions.HasAny && !TypeMatchedInclusions(property.DeclaringType, inclusions))
            return false;

        if (settings.PreserveXaml && isPublic &&
            !(property.GetMethod?.IsStatic ?? false) &&
            ObfuscatorHelpers.LooksLikeXamlBindable(property.DeclaringType))
            return false;

        if (!AllAccessorsRenamable(PropertyAccessors(property), settings, exclusions, inclusions, warnings))
            return false;

        return true;
    }

    private static bool IsIndexer(PropertyDef property) =>
        property.PropertySig?.Params.Count > 0;

    private static IEnumerable<MethodDef> PropertyAccessors(PropertyDef property)
    {
        if (property.GetMethod != null)
            yield return property.GetMethod;
        if (property.SetMethod != null)
            yield return property.SetMethod;
        foreach (var other in property.OtherMethods)
            yield return other;
    }

    private static IEnumerable<MethodDef> EventAccessors(EventDef evt)
    {
        if (evt.AddMethod != null)
            yield return evt.AddMethod;
        if (evt.RemoveMethod != null)
            yield return evt.RemoveMethod;
        if (evt.InvokeMethod != null)
            yield return evt.InvokeMethod;
        foreach (var other in evt.OtherMethods)
            yield return other;
    }

    private bool AllAccessorsRenamable(
        IEnumerable<MethodDef> accessors,
        SymbolRenamingSettings settings,
        ExclusionRules exclusions,
        InclusionRules inclusions,
        ICollection<string> warnings)
    {
        foreach (var accessor in accessors)
        {
            if (!CanRenameAccessor(accessor, settings, exclusions, inclusions, warnings))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Same visibility, override, interface, and exclusion rules as <see cref="CanRenameMethod"/>,
    /// except special-name is ignored because property and event accessors always carry it.
    /// </summary>
    private bool CanRenameAccessor(
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

        if (TakesPartInVirtualDispatch(method))
            return false;

        return true;
    }

    private static bool MatchesPattern(string value, string pattern) => WildcardMatcher.IsMatch(value, pattern);
}
