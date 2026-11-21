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
                if (IsStale(reference, set) is { } owner)
                {
                    findings.Add(
                        $"{(reference.IsFieldRef ? "field" : "method")} '{reference.Name}' of {owner.FullName} " +
                        $"(parent {reference.Class?.GetType().Name}, in {module.Name})");
                }
            }
        }

        return findings.Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
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

    private static TypeDef? ResolveTypeRef(TypeRef typeRef, IReadOnlyList<ModuleDef> set)
    {
        var assemblyName = typeRef.DefinitionAssembly?.Name?.String;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        foreach (var module in set)
        {
            if (module.Assembly?.Name?.String is not { } name
                || !name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return FindTypeDef(module, typeRef);
        }

        return null;
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
    /// reference's name and signature.
    /// </summary>
    private static bool HasMember(TypeDef owner, MemberRef reference, IReadOnlyList<ModuleDef> set)
    {
        for (var type = owner; type is not null; type = ResolveType(type.BaseType, set))
        {
            if (reference.IsFieldRef)
            {
                if (type.Fields.Any(f => f.Name == reference.Name && Comparer.Equals(f.FieldSig, reference.FieldSig)))
                    return true;
            }
            else if (type.Methods.Any(m => m.Name == reference.Name && Comparer.Equals(m.MethodSig, reference.MethodSig)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every MemberRef the module's code and metadata actually use: IL operands (directly or as the method of a
    /// method specification), custom attribute constructors and method overrides. A loaded output file also holds
    /// rows nothing refers to (the writer keeps the original MemberRef rows by RID next to the rewritten ones,
    /// <c>MetadataFlags.PreserveRids</c>); those are inert, so they are not looked at.
    /// </summary>
    private static IEnumerable<MemberRef> EnumerateMemberRefs(ModuleDef module)
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
