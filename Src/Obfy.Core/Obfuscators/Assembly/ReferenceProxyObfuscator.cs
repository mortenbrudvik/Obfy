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
/// compiler/interop/pointer/value-type/generic/<c>constrained.</c> sites are skipped.
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
            var proxyType = new TypeDefUser(
                "Obfy.Runtime",
                "<RefProxy>",
                module.CorLibTypes.Object.TypeDefOrRef)
            {
                Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
            };
            module.Types.Add(proxyType);

            var proxies = new Dictionary<string, MethodDef>(StringComparer.Ordinal);
            var created = 0;

            foreach (var type in module.GetTypes().ToList())
            {
                if (type == proxyType)
                    continue;
                // Keep helper internals as direct calls: a trampoline in <RefProxy> cannot ldftn
                // a private helper (MethodAccessException / unverifiable). User call sites still
                // proxy assembly-visible helper entry points. User private methods are still proxied.
                if (ObfuscatorHelpers.IsRuntimeHelper(type))
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
                        if (!CanProxy(called, module, context.Settings.Protection.ProxyExternalCalls))
                            continue;

                        var key = called.FullName + "|" + instr.OpCode.Code;
                        if (!proxies.TryGetValue(key, out var proxy))
                        {
                            proxy = CreateProxy(module, proxyType, called, instr.OpCode == OpCodes.Callvirt);
                            if (proxy == null)
                                continue;
                            proxyType.Methods.Add(proxy);
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

            if (created == 0)
                module.Types.Remove(proxyType);

            _logger.LogInformation("Created {Count} reference proxies", created);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Reference proxy injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Reference proxy injection failed: {ex.Message}", ex));
        }
    }

    private static bool CanProxy(IMethod called, ModuleDef module, bool proxyExternal)
    {
        if (called.Name == ".ctor" || called.Name == ".cctor")
            return false;
        if (called.MethodSig == null)
            return false;
        if (called.MethodSig.Params.Count > 16)
            return false;
        if (HasOpenGeneric(called))
            return false;
        if (called.DeclaringType == null)
            return false;
        if (called.DeclaringType.Name.String == "<RefProxy>")
            return false;

        var resolved = called.ResolveMethodDef();
        var inModule = resolved != null && resolved.Module == module;
        if (!inModule)
        {
            if (!proxyExternal)
                return false;
            return !IsUnsafeExternal(called);
        }

        if (resolved!.IsPinvokeImpl || resolved.IsNative)
            return false;

        return true;
    }

    private static bool IsUnsafeExternal(IMethod called)
    {
        var ns = called.DeclaringType?.Namespace ?? "";
        var typeName = called.DeclaringType?.Name.String ?? "";
        if (ns.StartsWith("System.Runtime.CompilerServices", StringComparison.Ordinal) ||
            ns.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal))
            return true;
        if (typeName is "RuntimeHelpers" or "Unsafe" or "Interlocked" or "Volatile" or "GCHandle" or "Buffer")
            return true;
        if (called.DeclaringType?.IsValueType == true)
            return true;

        var sig = called.MethodSig!;
        if (sig.CallingConvention == CallingConvention.VarArg)
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

    private static MethodDef? CreateProxy(ModuleDef module, TypeDef proxyType, IMethod target, bool virt)
    {
        var sig = target.MethodSig;
        if (sig == null)
            return null;

        var paramTypes = new List<TypeSig>();
        if (sig.HasThis)
        {
            TypeSig thisType = target.DeclaringType.ToTypeSig();
            if (thisType.IsValueType)
                thisType = new ByRefSig(thisType);
            paramTypes.Add(thisType);
        }

        paramTypes.AddRange(sig.Params);

        var proxySig = MethodSig.CreateStatic(sig.RetType, paramTypes.ToArray());
        var proxy = new MethodDefUser(
            "P" + proxyType.Methods.Count,
            proxySig,
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig);

        var body = new CilBody();
        proxy.Body = body;

        for (var i = 0; i < paramTypes.Count; i++)
        {
            body.Instructions.Add(i switch
            {
                0 => Instruction.Create(OpCodes.Ldarg_0),
                1 => Instruction.Create(OpCodes.Ldarg_1),
                2 => Instruction.Create(OpCodes.Ldarg_2),
                3 => Instruction.Create(OpCodes.Ldarg_3),
                _ => OpCodes.Ldarg.ToInstruction(i)
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
}
