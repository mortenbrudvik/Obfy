using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces direct call/callvirt targets with static <c>calli</c> trampolines so call sites no
/// longer reference the original method token. In-module calls are proxied when
/// <see cref="ProtectionSettings.ReferenceProxy"/> is on. Out-of-module calls (BCL and third-party)
/// are proxied only when <see cref="ProtectionSettings.ProxyExternalCalls"/> is also on;
/// compiler and interop types (including nested types), pointers, generics and
/// <c>constrained.</c> sites are skipped. An instance call is proxied only when the receiver is a
/// known reference type or a known value type; a value-type receiver is passed by reference.
/// Private, protected, and private-protected methods, and methods whose declaring type is not
/// visible to a sibling type, are trampolined from a nested type of the declaring type so
/// <c>ldftn</c> is legal. External proxies are keyed by assembly identity.
/// </summary>
public class ReferenceProxyObfuscator : IObfuscator
{
    private readonly ILogger<ReferenceProxyObfuscator> _logger;

    public ReferenceProxyObfuscator(ILogger<ReferenceProxyObfuscator> logger)
    {
        _logger = logger;
    }

    public string Name => "ReferenceProxy";

    public int Priority => (int)ObfuscationPhase.ReferenceProxy;

    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    public bool IsEnabled(ObfySettings settings) => settings.Protection.ReferenceProxy;

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();

        try
        {
            TypeDef? moduleProxy = null;
            var nestedProxies = new Dictionary<TypeDef, TypeDef>();
            var proxies = new Dictionary<string, MethodDef>(StringComparer.Ordinal);
            var receivers = ReceiverIndex.Build(module);
            var created = 0;

            foreach (var type in module.GetTypes().ToList())
            {
                if (moduleProxy != null && type == moduleProxy)
                    continue;
                // Do not rewrite calls inside helper types. CanProxy also rejects Vm.Run/Init
                // so the interpreter entry stays a direct call; other helper entry points
                // (decryptors) remain proxyable from user IL.
                if (ObfuscatorHelpers.IsRuntimeHelper(type))
                    continue;
                if (type.Name == "<RefProxy>")
                    continue;
                if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.All, context.Warnings))
                    continue;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!method.HasBody)
                        continue;
                    if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.All, context.Warnings))
                        continue;

                    var modified = false;
                    var instructions = method.Body.Instructions;
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var instr = instructions[i];
                        if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                            continue;
                        // constrained./tail./unaligned. may only prefix instance call/callvirt/ldvirtftn.
                        if (i > 0 && ObfuscatorHelpers.IsPrefix(instructions[i - 1]))
                            continue;
                        if (instr.Operand is not IMethod called)
                            continue;
                        if (!CanProxy(called, module, context, receivers, instr.OpCode == OpCodes.Callvirt))
                            continue;

                        var home = ClassifyProxy(called, module);
                        if (home == ProxyHome.Skip)
                            continue;

                        var owner = home == ProxyHome.Nested
                            ? GetNestedProxyType(called.ResolveMethodDef()!.DeclaringType, module, nestedProxies, context)
                            : ModuleProxy();

                        var key = ProxyKey(called, instr.OpCode.Code);
                        if (!proxies.TryGetValue(key, out var proxy))
                        {
                            proxy = CreateProxy(called, instr.OpCode == OpCodes.Callvirt, owner.Methods.Count, receivers);
                            if (proxy == null)
                                continue;
                            owner.Methods.Add(proxy);
                            proxies[key] = proxy;
                            created++;
                        }

                        instr.OpCode = OpCodes.Call;
                        instr.Operand = proxy;
                        modified = true;
                        stats.ProtectionsApplied++;
                    }

                    if (modified)
                    {
                        method.Body.KeepOldMaxStack = true;
                        method.Body.UpdateInstructionOffsets();
                    }
                }
            }

            _logger.LogInformation("Created {Count} reference proxies", created);
            return Task.FromResult(ObfuscationResult.Successful(stats));

            TypeDef ModuleProxy()
            {
                if (moduleProxy != null)
                    return moduleProxy;

                moduleProxy = new TypeDefUser(
                    "Obfy.Runtime",
                    "<RefProxy>",
                    module.CorLibTypes.Object.TypeDefOrRef)
                {
                    Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
                };
                RuntimeInjection.AddType(context, moduleProxy);
                return moduleProxy;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Reference proxy injection failed ({ExceptionType})", ex.GetType().Name);
            return Task.FromResult(ObfuscationResult.Failed($"Reference proxy injection failed: {ex.Message}", ex));
        }
    }

    private enum ProxyHome { Skip, Module, Nested }

    private static ProxyHome ClassifyProxy(IMethod called, ModuleDef module)
    {
        var resolved = called.ResolveMethodDef();
        var inModule = resolved != null && resolved.Module == module;
        if (!inModule)
        {
            if (resolved == null)
                return ProxyHome.Module;
            return CanReachFromModuleProxy(resolved, module) ? ProxyHome.Module : ProxyHome.Skip;
        }

        // A public method of a private nested type is still invisible to Obfy.Runtime.<RefProxy>.
        // The trampoline has to live inside the declaring type, or the call stays direct.
        if (NeedsNestedHome(resolved!))
        {
            // A nested trampoline inside an open generic type is not a stable ldftn target.
            // Leave the direct call so the original method still runs.
            if (DeclaringTypeIsGeneric(resolved!.DeclaringType))
                return ProxyHome.Skip;
            return ProxyHome.Nested;
        }

        return ProxyHome.Module;
    }

    private static bool NeedsNestedHome(MethodDef method)
    {
        var access = AccessOf(method);
        if (access is MethodAttributes.Private or MethodAttributes.Family or MethodAttributes.FamANDAssem)
            return true;

        return !VisibleToModuleProxy(method.DeclaringType);
    }

    /// <summary>
    /// A top-level sibling can see public and assembly types, and nested types that are themselves
    /// public or assembly, as long as every enclosing type is too. Private, protected and
    /// private-protected nested types are visible only from inside the enclosing type.
    /// </summary>
    private static bool VisibleToModuleProxy(TypeDef? type)
    {
        for (var current = type; current != null; current = current.DeclaringType)
        {
            if (current.DeclaringType == null)
                return true;

            var visibility = current.Attributes & TypeAttributes.VisibilityMask;
            if (visibility is TypeAttributes.NestedPrivate
                or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamANDAssem)
                return false;
        }

        return true;
    }

    private static bool CanReachFromModuleProxy(MethodDef resolved, ModuleDef module)
    {
        var access = AccessOf(resolved);
        if (access == MethodAttributes.Public)
            return true;

        if (!SameAssembly(resolved.Module, module))
            return false;

        return access is MethodAttributes.Assembly or MethodAttributes.FamORAssem;
    }

    private static MethodAttributes AccessOf(MethodDef method) =>
        method.Attributes & MethodAttributes.MemberAccessMask;

    private static bool SameAssembly(ModuleDef left, ModuleDef right)
    {
        if (ReferenceEquals(left.Assembly, right.Assembly) && left.Assembly != null)
            return true;

        var leftName = left.Assembly?.Name?.String;
        var rightName = right.Assembly?.Name?.String;
        return leftName != null &&
               rightName != null &&
               string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeclaringTypeIsGeneric(TypeDef? type)
    {
        for (var current = type; current != null; current = current.DeclaringType)
        {
            if (current.HasGenericParameters)
                return true;
        }

        return false;
    }

    private static TypeDef GetNestedProxyType(
        TypeDef declaring, ModuleDef module, Dictionary<TypeDef, TypeDef> cache, PipelineContext context)
    {
        if (cache.TryGetValue(declaring, out var existing))
            return existing;

        foreach (var nested in declaring.NestedTypes)
        {
            if (nested.Name == "<RefProxy>")
            {
                cache[declaring] = nested;
                return nested;
            }
        }

        var created = new TypeDefUser(
            UTF8String.Empty,
            "<RefProxy>",
            module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedAssembly | TypeAttributes.Sealed | TypeAttributes.Abstract | TypeAttributes.BeforeFieldInit
        };
        declaring.NestedTypes.Add(created);
        RuntimeInjection.Register(context, created);
        cache[declaring] = created;
        return created;
    }

    /// <summary>
    /// <see cref="IMethod.FullName"/> is not assembly-qualified. Two external methods that share
    /// a display name must not share a trampoline.
    /// </summary>
    private static string ProxyKey(IMethod called, Code code) =>
        ScopeKey(called) + "|" + called.FullName + "|" + (int)code;

    private static string ScopeKey(IMethod called)
    {
        var declaring = called.DeclaringType;
        if (declaring == null)
            return "";

        if (declaring.Scope is AssemblyRef assemblyRef && !UTF8String.IsNullOrEmpty(assemblyRef.Name))
            return assemblyRef.FullName;

        var defined = declaring.DefinitionAssembly;
        if (defined != null && !UTF8String.IsNullOrEmpty(defined.Name))
            return defined.FullName;

        if (declaring.Scope is ModuleRef moduleRef)
            return "module:" + moduleRef.Name;

        return declaring.Scope?.ToString() ?? "";
    }

    private static bool CanProxy(
        IMethod called, ModuleDef module, PipelineContext context, ReceiverIndex receivers, bool virtualCall)
    {
        if (called.Name == ".ctor" || called.Name == ".cctor")
            return false;
        if (called.MethodSig == null)
            return false;
        if (called.MethodSig.Params.Count > 16)
            return false;
        // The trampoline and its calli use the default managed convention; a vararg call site
        // (sentinel + trailing args) cannot be forwarded that way, in or out of this module.
        if (IsVarArg(called.MethodSig))
            return false;
        if (HasOpenGeneric(called))
            return false;
        if (called.DeclaringType == null)
            return false;
        if (called.DeclaringType.Name.String == "<RefProxy>")
            return false;

        var resolved = called.ResolveMethodDef();
        var declaringType = resolved?.DeclaringType ?? called.DeclaringType.ResolveTypeDef();
        if (declaringType is not null &&
            declaringType.Name == "Vm" &&
            (declaringType.Namespace == "Obfy.Runtime" || ObfuscatorHelpers.IsRuntimeHelper(declaringType)))
            return false;

        var inModule = resolved != null && resolved.Module == module;
        if (!inModule)
        {
            if (!context.Settings.Protection.ProxyExternalCalls)
                return false;
            if (IsUnsafeExternal(called))
                return false;
        }
        else if (resolved!.IsPinvokeImpl || resolved.IsNative)
        {
            return false;
        }

        // The call site pushes an object reference for a class and a managed pointer for a value
        // type. The trampoline can repeat that only when the kind is known. callvirt on a value
        // type needs a constrained. prefix, which is not a legal ldvirtftn.
        if (called.MethodSig.HasThis)
        {
            var kind = receivers.KindOf(called.DeclaringType);
            if (kind != ReceiverKind.Reference && kind != ReceiverKind.ValueType)
                return false;
            if (virtualCall && kind == ReceiverKind.ValueType)
                return false;
        }

        return true;
    }

    private static bool IsUnsafeExternal(IMethod called)
    {
        if (IsCompilerOrInterop(called.DeclaringType))
            return true;

        var sig = called.MethodSig!;
        if (IsVarArg(sig))
            return true;
        if (IsPointerLike(sig.RetType))
            return true;
        foreach (var p in sig.Params)
        {
            if (IsPointerLike(p))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A nested type's <c>TypeRef</c> stores an empty namespace. The namespace lives on the
    /// enclosing type, so <c>YieldAwaitable/YieldAwaiter</c> is still a compiler type.
    /// </summary>
    private static bool IsCompilerOrInterop(ITypeDefOrRef? type)
    {
        for (var depth = 0; type != null && depth < 16; depth++, type = Enclosing(type))
        {
            var ns = type.Namespace ?? "";
            if (ns.StartsWith("System.Runtime.CompilerServices", StringComparison.Ordinal) ||
                ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal))
                return true;

            var name = type.Name?.String ?? "";
            if (name is "RuntimeHelpers" or "Unsafe" or "Interlocked" or "Volatile" or "GCHandle" or "Buffer")
                return true;
        }

        return false;
    }

    private static ITypeDefOrRef? Enclosing(ITypeDefOrRef type) => type switch
    {
        TypeDef defined => defined.DeclaringType,
        TypeRef reference => reference.DeclaringType,
        TypeSpec spec when spec.ScopeType is { } scope && !ReferenceEquals(scope, spec) => scope,
        _ => null
    };

    /// <summary>
    /// The convention byte also carries HasThis/Generic flags (an instance vararg method is
    /// <c>VarArg | HasThis</c>), so compare the masked convention.
    /// </summary>
    private static bool IsVarArg(MethodBaseSig sig) =>
        (sig.CallingConvention & CallingConvention.Mask) == CallingConvention.VarArg;

    private static bool IsPointerLike(TypeSig? sig) =>
        sig?.ElementType is ElementType.Ptr or ElementType.FnPtr or ElementType.ByRef;

    private static bool HasOpenGeneric(IMethod called)
    {
        if (called is MethodSpec || called.MethodSig.GenParamCount > 0)
            return true;
        if (called.DeclaringType is TypeSpec)
            return true;
        return ContainsGeneric(called.MethodSig.RetType) ||
               called.MethodSig.Params.Any(ContainsGeneric);
    }

    private static bool ContainsGeneric(TypeSig? sig)
    {
        while (sig != null)
        {
            if (sig.ElementType is ElementType.Var or ElementType.MVar)
                return true;
            if (sig is GenericInstSig)
                return true;
            sig = sig.Next;
        }
        return false;
    }

    private static MethodDef? CreateProxy(IMethod target, bool virt, int nameIndex, ReceiverIndex receivers)
    {
        var sig = target.MethodSig;
        if (sig == null || target.DeclaringType == null)
            return null;

        var paramTypes = new List<TypeSig>();
        if (sig.HasThis)
        {
            var kind = receivers.KindOf(target.DeclaringType);
            if (kind != ReceiverKind.Reference && kind != ReceiverKind.ValueType)
                return null;
            if (virt && kind == ReceiverKind.ValueType)
                return null;
            paramTypes.Add(ReceiverParameter(target.DeclaringType, kind));
        }

        paramTypes.AddRange(sig.Params);

        var proxySig = MethodSig.CreateStatic(sig.RetType, paramTypes.ToArray());
        var proxy = new MethodDefUser(
            "P" + nameIndex,
            proxySig,
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig);

        var body = new CilBody();
        proxy.Body = body;

        // ldarg takes a parameter operand (a uint16 index), not an int32. Ldarg.ToInstruction(int)
        // throws once a trampoline has five or more arguments, which aborts the whole run.
        for (var i = 0; i < paramTypes.Count; i++)
        {
            var parameter = proxy.Parameters[i];
            body.Instructions.Add(parameter.Index switch
            {
                0 => Instruction.Create(OpCodes.Ldarg_0),
                1 => Instruction.Create(OpCodes.Ldarg_1),
                2 => Instruction.Create(OpCodes.Ldarg_2),
                3 => Instruction.Create(OpCodes.Ldarg_3),
                <= byte.MaxValue => Instruction.Create(OpCodes.Ldarg_S, parameter),
                _ => Instruction.Create(OpCodes.Ldarg, parameter)
            });
        }

        if (virt && sig.HasThis)
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(virt ? OpCodes.Ldvirtftn : OpCodes.Ldftn, target));
        var calliSig = sig.HasThis
            ? MethodSig.CreateInstance(sig.RetType, sig.Params.ToArray())
            : MethodSig.CreateStatic(sig.RetType, sig.Params.ToArray());
        body.Instructions.Add(Instruction.Create(OpCodes.Calli, calliSig));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.KeepOldMaxStack = true;
        body.MaxStack = (ushort)Math.Max(8, paramTypes.Count + 2);
        body.UpdateInstructionOffsets();
        return proxy;
    }

    /// <summary>
    /// The receiver the call site already pushed: the value type's address, or the object reference.
    /// A corlib primitive stays a primitive (<c>int32&amp;</c>), not a <c>valuetype</c> token.
    /// </summary>
    private static TypeSig ReceiverParameter(ITypeDefOrRef declaring, ReceiverKind kind)
    {
        if (declaring.Module?.CorLibTypes.GetCorLibTypeSig(declaring) is { } corLib)
            return corLib.IsValueType ? new ByRefSig(corLib) : corLib;

        return kind == ReceiverKind.ValueType
            ? new ByRefSig(new ValueTypeSig(declaring))
            : new ClassSig(declaring);
    }

    private enum ReceiverKind { Unknown, Reference, ValueType }

    /// <summary>
    /// How each type is used in this module. <see cref="TypeRef.IsValueType"/> is false when the
    /// assembly cannot be resolved, and a nested type's namespace is empty, so the signatures
    /// already in the module are the other source: a <c>valuetype</c> encoding is a value type.
    /// </summary>
    private sealed class ReceiverIndex
    {
        private readonly Dictionary<string, ReceiverKind> _kinds = new(StringComparer.Ordinal);

        public static ReceiverIndex Build(ModuleDef module)
        {
            var index = new ReceiverIndex();
            foreach (var member in module.GetMemberRefs())
            {
                if (member.MethodSig != null)
                    index.Note(member.MethodSig);
                else
                    index.Note(member.FieldSig?.Type);
            }

            foreach (var type in module.GetTypes())
            {
                foreach (var field in type.Fields)
                    index.Note(field.FieldSig?.Type);
                foreach (var method in type.Methods)
                {
                    index.Note(method.MethodSig);
                    if (!method.HasBody)
                        continue;
                    foreach (var local in method.Body.Variables)
                        index.Note(local.Type);
                }
            }

            return index;
        }

        public ReceiverKind KindOf(ITypeDefOrRef? type)
        {
            if (type == null)
                return ReceiverKind.Unknown;

            if (type is TypeSpec spec)
                return KindOfSignature(spec.TypeSig);

            if (type.Module?.CorLibTypes.GetCorLibTypeSig(type) is { } corLib)
                return corLib.IsValueType ? ReceiverKind.ValueType : ReceiverKind.Reference;

            if (type is TypeDef defined)
                return defined.IsValueType ? ReceiverKind.ValueType : ReceiverKind.Reference;

            if (type is TypeRef reference)
            {
                var resolved = reference.Resolve();
                if (resolved != null)
                    return resolved.IsValueType ? ReceiverKind.ValueType : ReceiverKind.Reference;
            }

            return _kinds.TryGetValue(Key(type), out var kind) ? kind : ReceiverKind.Unknown;
        }

        private static ReceiverKind KindOfSignature(TypeSig? sig)
        {
            sig = sig?.RemovePinnedAndModifiers();
            if (sig is GenericInstSig generic)
            {
                if (generic.GenericType is ValueTypeSig)
                    return ReceiverKind.ValueType;
                if (generic.GenericType is ClassSig)
                    return ReceiverKind.Reference;
                return ReceiverKind.Unknown;
            }

            if (sig == null)
                return ReceiverKind.Unknown;
            if (sig.IsValueType || sig.ElementType == ElementType.ValueType)
                return ReceiverKind.ValueType;
            if (sig.ElementType is ElementType.Class or ElementType.Object or ElementType.String)
                return ReceiverKind.Reference;
            return ReceiverKind.Unknown;
        }

        private void Note(MethodBaseSig? sig)
        {
            if (sig == null)
                return;
            Note(sig.RetType);
            foreach (var parameter in sig.Params)
                Note(parameter);
            if (sig.ParamsAfterSentinel == null)
                return;
            foreach (var parameter in sig.ParamsAfterSentinel)
                Note(parameter);
        }

        private void Note(TypeSig? sig)
        {
            for (var depth = 0; sig != null && depth < 32; depth++)
            {
                sig = sig.RemovePinnedAndModifiers();
                switch (sig)
                {
                    case null:
                        return;
                    case GenericInstSig generic:
                        Note(generic.GenericType);
                        foreach (var argument in generic.GenericArguments)
                            Note(argument);
                        return;
                    case FnPtrSig function:
                        Note(function.MethodSig);
                        return;
                    case ValueTypeSig valueType:
                        Remember(valueType.TypeDefOrRef, ReceiverKind.ValueType);
                        return;
                    case ClassSig reference:
                        Remember(reference.TypeDefOrRef, ReceiverKind.Reference);
                        return;
                    default:
                        sig = sig.Next;
                        break;
                }
            }
        }

        private void Remember(ITypeDefOrRef? type, ReceiverKind kind)
        {
            if (type == null || type is TypeSpec)
                return;

            var key = Key(type);
            if (_kinds.TryGetValue(key, out var existing) && existing != kind)
            {
                _kinds[key] = ReceiverKind.Unknown;
                return;
            }

            _kinds[key] = kind;
        }

        private static string Key(ITypeDefOrRef type)
        {
            var scope = type.Scope switch
            {
                AssemblyRef assembly => assembly.FullName,
                ModuleRef moduleRef => "module:" + moduleRef.Name.String,
                ModuleDef module when module.Assembly != null => module.Assembly.FullName,
                _ => type.Scope?.ToString() ?? ""
            };
            return scope + "|" + type.FullName;
        }
    }
}
