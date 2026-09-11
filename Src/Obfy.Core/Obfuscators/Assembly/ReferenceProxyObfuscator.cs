using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces direct call/callvirt targets with static proxy methods so call sites no longer
/// reference the original method token.
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
                if (ObfuscatorHelpers.IsRuntimeHelper(type))
                    continue;
                if (ObfuscatorHelpers.IsExcluded(type, context.Settings.Exclusions))
                    continue;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!method.HasBody)
                        continue;
                    if (ObfuscatorHelpers.MethodMatchesExclusion(method, context.Settings.Exclusions))
                        continue;

                    var modified = false;
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                            continue;
                        if (instr.Operand is not IMethod called)
                            continue;
                        if (!CanProxy(called, module))
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reference proxy injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Reference proxy injection failed: {ex.Message}", ex));
        }
    }

    private static bool CanProxy(IMethod called, ModuleDef module)
    {
        if (called.Name == ".ctor" || called.Name == ".cctor")
            return false;
        if (called.MethodSig == null)
            return false;
        if (called.MethodSig.Params.Count > 16)
            return false;
        if (called.MethodSig.GenParamCount > 0 || called is MethodSpec)
            return false;
        if (called.DeclaringType == null)
            return false;
        if (called.DeclaringType.Name.String == "<RefProxy>")
            return false;

        // Only proxy methods defined in this module. MemberRefs into corlib (e.g. String.get_Length)
        // are easy to get wrong (this-pointer TypeSig) and are not the calls we need to hide.
        var resolved = called.ResolveMethodDef();
        if (resolved == null || resolved.Module != module)
            return false;
        if (ObfuscatorHelpers.IsRuntimeHelper(resolved.DeclaringType))
            return false;
        if (resolved.IsPinvokeImpl || resolved.IsNative)
            return false;

        return true;
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
