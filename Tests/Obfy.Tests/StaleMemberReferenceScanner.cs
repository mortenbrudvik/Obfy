using dnlib.DotNet;

namespace Obfy.Tests;

/// <summary>
/// Safety net for symbol renaming: finds member references (MemberRefs the code or metadata uses, however their
/// parent is encoded) that point at a definition of the scanned module or closed set but name a member that the
/// definition no longer has. The CLR binds such a reference by name and signature when the calling method
/// is compiled, so each finding is a <c>MissingFieldException</c> / <c>MissingMethodException</c> waiting
/// for the code path that uses it.
/// <para>
/// A reference to a def is a MemberRef whose class is the method itself (a vararg call site), the declaring
/// type (<see cref="TypeDef"/>), a <see cref="TypeRef"/> to an assembly of the set, or a <see cref="TypeSpec"/>
/// naming one of those as the generic type of an instantiation. Array, pointer and by-ref parents are the
/// runtime's own members and are never looked up.
/// </para>
/// </summary>
internal static class StaleMemberReferenceScanner
{
    private static readonly SigComparer Comparer = new(SigComparerOptions.DontCompareTypeScope);

    /// <summary>Scans one module; its own definitions are the ones references may point at.</summary>
    public static List<string> Find(ModuleDef module) => Find(new[] { module });

    /// <summary>Scans every module of a closed set; any of them may define what another one references.</summary>
    public static List<string> Find(IReadOnlyList<ModuleDef> set)
    {
        var findings = new List<string>();
        foreach (var module in set)
        {
            foreach (var reference in EnumerateMemberRefs(module))
            {
                var kind = reference.IsFieldRef ? "field" : "method";
                if (IsStale(reference, set) is { } owner)
                {
                    findings.Add(
                        $"{kind} '{reference.Name}' of {owner.FullName} " +
                        $"(parent {reference.Class?.GetType().Name}, in {module.Name})");
                }

                if (UnresolvableParent(reference, set) is { } parent)
                    findings.Add($"{kind} '{reference.Name}' on {parent.FullName}, which no type of its assembly is named (in {module.Name})");
            }

            findings.AddRange(FindUnresolvableTypeRefs(module, set));
            findings.AddRange(FindStaleNamedArguments(module, set));
        }

        return findings.Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The type reference a member reference names as its parent (or as the generic type of its parent), when it names an
    /// assembly of the set but no type of it: a parent that cannot be resolved there is a finding, not "somebody else's type".
    /// A framework type or a type of an assembly outside the set is not.
    /// </summary>
    private static TypeRef? UnresolvableParent(MemberRef reference, IReadOnlyList<ModuleDef> set)
    {
        var parent = reference.Class switch
        {
            TypeRef typeRef => typeRef,
            TypeSpec { TypeSig: GenericInstSig { GenericType.TypeDefOrRef: TypeRef generic } } => generic,
            TypeSpec { TypeSig: ClassOrValueTypeSig { TypeDefOrRef: TypeRef plain } } => plain,
            _ => null
        };
        return parent is not null && NamesNothingOfItsAssembly(parent, set) ? parent : null;
    }

    /// <summary>
    /// Every type reference of the module that names an assembly of the set but no type of it: the name of a type
    /// the renamer renamed was not rewritten in the reference, which no longer finds the type when it is loaded.
    /// </summary>
    private static IEnumerable<string> FindUnresolvableTypeRefs(ModuleDef module, IReadOnlyList<ModuleDef> set)
    {
        foreach (var typeRef in module.GetTypeRefs())
        {
            if (NamesNothingOfItsAssembly(typeRef, set))
                yield return $"type reference {typeRef.FullName} names no type of its assembly (in {module.Name})";
        }
    }

    private static bool NamesNothingOfItsAssembly(TypeRef typeRef, IReadOnlyList<ModuleDef> set)
    {
        var target = SetModuleOf(typeRef, set);
        if (target is null || FindTypeDef(target, typeRef) is not null)
            return false;

        // A type the assembly forwards elsewhere is found by the runtime through the forwarder.
        return !target.ExportedTypes.Any(e => e.TypeName == typeRef.Name && e.TypeNamespace == typeRef.Namespace);
    }

    /// <summary>
    /// A named argument of a custom attribute (<c>[Info(Note = "x")]</c>) is stored as the NAME of a field or property
    /// of the attribute type, which the CLR looks up when the attribute is read. Reports every named argument
    /// of an attribute defined in the set that names no field or property of the attribute type or its base
    /// types. Every row of the CustomAttribute table is looked at, whatever its parent is, so no owner can be
    /// forgotten the way a hand-written list of them could.
    /// </summary>
    private static IEnumerable<string> FindStaleNamedArguments(ModuleDef module, IReadOnlyList<ModuleDef> set)
    {
        foreach (var (owner, attributeType, arguments) in AllNamedArguments(module))
        {
            foreach (var argument in arguments)
            {
                if (IsStaleNamedArgument(attributeType, argument, set) is { } attributeDef)
                {
                    yield return $"named argument '{argument.Name}' of {attributeDef.FullName} " +
                        $"(on {owner.GetType().Name}, in {module.Name})";
                }
            }
        }
    }

    private static TypeDef? IsStaleNamedArgument(ITypeDefOrRef? attributeType, CANamedArgument argument, IReadOnlyList<ModuleDef> set)
    {
        var attributeDef = ResolveType(attributeType, set);
        if (attributeDef is null)
            return null;

        for (var type = attributeDef; type is not null; type = ResolveType(type.BaseType, set))
        {
            if (argument.IsField
                    ? type.Fields.Any(f => f.Name == argument.Name)
                    : type.Properties.Any(p => p.Name == argument.Name))
                return null;
        }

        return attributeDef;
    }

    /// <summary>The named arguments of every custom attribute and declarative security attribute of the module.</summary>
    private static IEnumerable<(object Owner, ITypeDefOrRef? AttributeType, IList<CANamedArgument> Arguments)> AllNamedArguments(
        ModuleDef module)
    {
        if (module is ModuleDefMD md)
        {
            var seen = new HashSet<uint>();
            var table = md.TablesStream.CustomAttributeTable;
            for (uint rid = 1; rid <= table.Rows; rid++)
            {
                if (!md.TablesStream.TryReadCustomAttributeRow(rid, out var row))
                    continue;
                var owner = md.ResolveHasCustomAttribute(row.Parent);
                if (owner is null || !seen.Add(owner.MDToken.Raw))
                    continue;
                foreach (var attribute in owner.CustomAttributes)
                    yield return (owner, attribute.AttributeType, attribute.NamedArguments);
            }

            for (uint rid = 1; rid <= md.TablesStream.DeclSecurityTable.Rows; rid++)
            {
                var security = md.ResolveDeclSecurity(rid);
                foreach (var attribute in security.SecurityAttributes)
                    yield return (security, attribute.AttributeType, attribute.NamedArguments);
            }

            yield break;
        }

        foreach (var owner in CustomAttributeOwners(module))
        {
            foreach (var attribute in owner.CustomAttributes)
                yield return (owner, attribute.AttributeType, attribute.NamedArguments);
        }
    }

    /// <summary>
    /// The definition the reference points at when no member of it, or of a base type in the set, has the
    /// reference's name and signature; null when the reference is fine or does not point into the set.
    /// </summary>
    internal static TypeDef? IsStale(MemberRef reference, IReadOnlyList<ModuleDef> set)
    {
        // A vararg call site names its target through a MemberRef whose parent is the MethodDef itself.
        if (reference.Class is MethodDef vararg)
            return vararg.Name == reference.Name ? null : vararg.DeclaringType;

        var owner = ResolveOwner(reference, set);
        return owner is not null && !HasMember(owner, reference, set) ? owner : null;
    }

    /// <summary>
    /// The definition a reference's parent names inside the set, or null (framework type, array, pointer,
    /// a type outside the set).
    /// </summary>
    private static TypeDef? ResolveOwner(MemberRef reference, IReadOnlyList<ModuleDef> set) =>
        reference.Class switch
        {
            TypeDef def => set.Any(m => ReferenceEquals(m, def.Module)) ? def : null,
            TypeRef typeRef => ResolveTypeRef(typeRef, set),
            TypeSpec spec => ResolveTypeSig(spec.TypeSig, set),
            _ => null
        };

    private static TypeDef? ResolveTypeSig(TypeSig? sig, IReadOnlyList<ModuleDef> set) =>
        sig switch
        {
            GenericInstSig generic => ResolveType(generic.GenericType?.TypeDefOrRef, set),
            ClassOrValueTypeSig plain => ResolveType(plain.TypeDefOrRef, set),
            _ => null
        };

    private static TypeDef? ResolveType(ITypeDefOrRef? type, IReadOnlyList<ModuleDef> set) =>
        type switch
        {
            TypeDef def => set.Any(m => ReferenceEquals(m, def.Module)) ? def : null,
            TypeRef typeRef => ResolveTypeRef(typeRef, set),
            TypeSpec spec => ResolveTypeSig(spec.TypeSig, set),
            _ => null
        };

    private static TypeDef? ResolveTypeRef(TypeRef typeRef, IReadOnlyList<ModuleDef> set) =>
        SetModuleOf(typeRef, set) is { } module ? FindTypeDef(module, typeRef) : null;

    /// <summary>The module of the set whose assembly the type reference names, or null (framework, outside the set).</summary>
    private static ModuleDef? SetModuleOf(TypeRef typeRef, IReadOnlyList<ModuleDef> set)
    {
        var assemblyName = typeRef.DefinitionAssembly?.Name?.String;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        return set.FirstOrDefault(module => module.Assembly?.Name?.String is { } name
            && name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static TypeDef? FindTypeDef(ModuleDef module, TypeRef typeRef)
    {
        if (typeRef.ResolutionScope is TypeRef enclosingRef)
        {
            var enclosing = FindTypeDef(module, enclosingRef);
            return enclosing?.NestedTypes.FirstOrDefault(n => n.Name == typeRef.Name);
        }

        return module.Types.FirstOrDefault(t => t.Name == typeRef.Name && t.Namespace == typeRef.Namespace);
    }

    /// <summary>
    /// Whether <paramref name="owner"/>, or a base type inside the set, declares a field or method with the
    /// reference's name and signature. A member of a base type is compared the way the CLR does it: with the generic
    /// arguments of the base type's instantiation substituted (<c>T Get()</c> of <c>Box&lt;T&gt;</c> is <c>int32 ()</c> for
    /// <c>IntBox : Box&lt;int&gt;</c>), expressed in the owner's own generic parameters.
    /// </summary>
    private static bool HasMember(TypeDef owner, MemberRef reference, IReadOnlyList<ModuleDef> set)
    {
        IList<TypeSig>? arguments = null; // what the generic parameters of `type` stand for; null: as declared
        var seen = new HashSet<TypeDef>();
        for (var type = owner; type is not null && seen.Add(type);)
        {
            if (reference.IsFieldRef)
            {
                if (type.Fields.Any(f => f.Name == reference.Name
                        && Comparer.Equals(arguments is null ? f.FieldSig : TypeSigSubstitution.Substitute(f.FieldSig, arguments), reference.FieldSig)))
                    return true;
            }
            else if (type.Methods.Any(m => m.Name == reference.Name
                         && Comparer.Equals(arguments is null ? m.MethodSig : TypeSigSubstitution.Substitute(m.MethodSig, arguments), reference.MethodSig)))
            {
                return true;
            }

            // The next type up and what its generic parameters stand for.
            var baseType = type.BaseType;
            var next = ResolveType(baseType, set);
            if (baseType is TypeSpec { TypeSig: GenericInstSig instantiation })
            {
                arguments = arguments is null
                    ? instantiation.GenericArguments.ToList()
                    : instantiation.GenericArguments.Select(a => TypeSigSubstitution.Substitute(a, arguments)!).ToList();
            }
            else
            {
                arguments = null;
            }

            type = next;
        }

        return false;
    }

    /// <summary>
    /// Every MemberRef the module's code and metadata actually use: IL operands (directly or as the method of a
    /// method specification), custom attribute constructors and method overrides. A row nothing refers to is
    /// inert, so it is not looked at (the output holds none that carries an original name:
    /// <c>SymbolRenamingLeftoverNamesTests</c>).
    /// </summary>
    internal static IEnumerable<MemberRef> EnumerateMemberRefs(ModuleDef module)
    {
        foreach (var owner in CustomAttributeOwners(module))
        {
            foreach (var attribute in owner.CustomAttributes)
                if (attribute.Constructor is MemberRef ctor)
                    yield return ctor;
        }

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                foreach (var over in method.Overrides)
                {
                    if (over.MethodDeclaration is MemberRef declaration)
                        yield return declaration;
                    if (over.MethodBody is MemberRef body)
                        yield return body;
                }

                if (!method.HasBody)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MemberRef memberRef:
                            yield return memberRef;
                            break;
                        case MethodSpec { Method: MemberRef viaSpec }:
                            yield return viaSpec;
                            break;
                    }
                }
            }
        }
    }

    private static IEnumerable<IHasCustomAttribute> CustomAttributeOwners(ModuleDef module)
    {
        yield return module;
        if (module.Assembly is { } assembly)
            yield return assembly;

        foreach (var type in module.GetTypes())
        {
            yield return type;
            foreach (var parameter in type.GenericParameters)
                yield return parameter;

            foreach (var field in type.Fields)
                yield return field;
            foreach (var property in type.Properties)
                yield return property;
            foreach (var evt in type.Events)
                yield return evt;

            foreach (var method in type.Methods)
            {
                yield return method;
                foreach (var parameter in method.GenericParameters)
                    yield return parameter;
                foreach (var parameter in method.ParamDefs)
                    yield return parameter;
            }
        }
    }
}
