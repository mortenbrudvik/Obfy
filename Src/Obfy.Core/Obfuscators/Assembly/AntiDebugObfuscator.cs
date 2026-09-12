using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects anti-debugging protection into assemblies.
/// </summary>
public class AntiDebugObfuscator : IObfuscator
{
    private enum FailKind { Exit = 0, FailFast = 1, Throw = 2 }

    private readonly ILogger<AntiDebugObfuscator> _logger;
    private FailKind _nextFail;

    public AntiDebugObfuscator(ILogger<AntiDebugObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "AntiDebug";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.AntiDebug;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiDebug;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.Protection;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting anti-debug protection injection");

        try
        {
            if (settings.AntiDebug)
            {
                var antiDebugType = InjectAntiDebugType(module);

                var moduleInitializer = FindModuleInitializer(module) ?? CreateModuleInitializer(module);
                if (InjectDebuggerCheck(moduleInitializer, antiDebugType))
                    stats.ProtectionsApplied++;

                foreach (var type in module.GetTypes())
                {
                    if (type == antiDebugType || ObfuscatorHelpers.IsRuntimeHelper(type))
                        continue;

                    foreach (var method in type.Methods)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (InjectDebuggerCheck(method, antiDebugType))
                            stats.ProtectionsApplied++;
                    }
                }

                if (stats.ProtectionsApplied == 0)
                {
                    const string warning =
                        "Anti-debug: runtime type was injected but no method body could be instrumented (all P/Invoke/abstract/empty/prefix-only).";
                    context.Warnings.Add(warning);
                    _logger.LogWarning("{Warning}", warning);
                }
            }

            _logger.LogInformation("Applied {Count} anti-debug protections", stats.ProtectionsApplied);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Anti-debug injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Anti-debug injection failed: {ex.Message}", ex));
        }
    }

    private TypeDef InjectAntiDebugType(ModuleDef module)
    {
        // Create internal static class for anti-debug
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<AntiDebug>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        var isDebuggerPresent = CreateKernel32PInvoke(
            module, "IsDebuggerPresent", MethodSig.CreateStatic(module.CorLibTypes.Boolean));
        var getCurrentProcess = CreateKernel32PInvoke(
            module, "GetCurrentProcess", MethodSig.CreateStatic(module.CorLibTypes.IntPtr));
        var checkRemote = CreateKernel32PInvoke(
            module, "CheckRemoteDebuggerPresent",
            MethodSig.CreateStatic(
                module.CorLibTypes.Boolean,
                module.CorLibTypes.IntPtr,
                new ByRefSig(module.CorLibTypes.Boolean)));

        typeDef.Methods.Add(isDebuggerPresent);
        typeDef.Methods.Add(getCurrentProcess);
        typeDef.Methods.Add(checkRemote);

        var checkMethod = CreateCheckDebuggerMethod(module, isDebuggerPresent, getCurrentProcess, checkRemote);
        typeDef.Methods.Add(checkMethod);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private static MethodDef CreateKernel32PInvoke(ModuleDef module, string name, MethodSig sig)
    {
        return new MethodDefUser(
            name,
            sig,
            MethodImplAttributes.PreserveSig,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl)
        {
            ImplMap = new ImplMapUser(
                new ModuleRefUser(module, "kernel32"),
                name,
                PInvokeAttributes.SupportsLastError | PInvokeAttributes.CallConvWinapi | PInvokeAttributes.NoMangle)
        };
    }

    private MethodDef CreateCheckDebuggerMethod(
        ModuleDef module,
        MethodDef isDebuggerPresent,
        MethodDef getCurrentProcess,
        MethodDef checkRemote)
    {
        var method = new MethodDefUser(
            "Check",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var remoteLocal = new Local(module.CorLibTypes.Boolean);
        var tickLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(remoteLocal);
        body.Variables.Add(tickLocal);

        var debuggerType = module.CorLibTypes.GetTypeRef("System.Diagnostics", "Debugger");
        var isAttachedGetter = new MemberRefUser(
            module, "get_IsAttached",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean), debuggerType);
        var isLogging = new MemberRefUser(
            module, "IsLogging",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean), debuggerType);
        var env = module.CorLibTypes.GetTypeRef("System", "Environment");
        var getTickCount = new MemberRefUser(
            module, "get_TickCount",
            MethodSig.CreateStatic(module.CorLibTypes.Int32), env);
        var dllNotFound = module.CorLibTypes.GetTypeRef("System", "DllNotFoundException");
        var entryNotFound = module.CorLibTypes.GetTypeRef("System", "EntryPointNotFoundException");

        var afterAttached = Instruction.Create(OpCodes.Nop);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, isAttachedGetter));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, afterAttached));
        EmitFail(body, module);
        body.Instructions.Add(afterAttached);

        var afterLogging = Instruction.Create(OpCodes.Nop);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, isLogging));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, afterLogging));
        EmitFail(body, module);
        body.Instructions.Add(afterLogging);

        var tryStart = Instruction.Create(OpCodes.Call, isDebuggerPresent);
        var afterPresent = Instruction.Create(OpCodes.Call, getCurrentProcess);
        var afterRemote = Instruction.Create(OpCodes.Nop);
        var catchDll = Instruction.Create(OpCodes.Pop);
        var catchEntry = Instruction.Create(OpCodes.Pop);
        var afterTry = Instruction.Create(OpCodes.Call, getTickCount);

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, afterPresent));
        EmitFail(body, module);

        body.Instructions.Add(afterPresent);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, remoteLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, remoteLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, checkRemote));
        var afterProtectOk = Instruction.Create(OpCodes.Ldloc, remoteLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, afterRemote));
        body.Instructions.Add(afterProtectOk);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, afterRemote));
        EmitFail(body, module);
        body.Instructions.Add(afterRemote);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, afterTry));

        body.Instructions.Add(catchDll);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, afterTry));
        body.Instructions.Add(catchEntry);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, afterTry));

        body.Instructions.Add(afterTry);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, tickLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, tickLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0x5A5A));
        body.Instructions.Add(Instruction.Create(OpCodes.Xor));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getTickCount));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, tickLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        var afterTiming = Instruction.Create(OpCodes.Ret);
        body.Instructions.Add(Instruction.CreateLdcI4(1000));
        body.Instructions.Add(Instruction.Create(OpCodes.Ble, afterTiming));
        EmitFail(body, module);
        body.Instructions.Add(afterTiming);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchDll,
            HandlerStart = catchDll,
            HandlerEnd = catchEntry,
            CatchType = dllNotFound
        });
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchDll,
            HandlerStart = catchEntry,
            HandlerEnd = afterTry,
            CatchType = entryNotFound
        });

        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }

    private void EmitFail(CilBody body, ModuleDef module)
    {
        var environmentType = module.CorLibTypes.GetTypeRef("System", "Environment");
        switch (_nextFail)
        {
            case FailKind.Exit:
                var exit = new MemberRefUser(
                    module, "Exit",
                    MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.Int32),
                    environmentType);
                body.Instructions.Add(Instruction.CreateLdcI4(1));
                body.Instructions.Add(Instruction.Create(OpCodes.Call, exit));
                break;
            case FailKind.FailFast:
                var failFast = new MemberRefUser(
                    module, "FailFast",
                    MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.String),
                    environmentType);
                body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ""));
                body.Instructions.Add(Instruction.Create(OpCodes.Call, failFast));
                break;
            default:
                var exType = module.CorLibTypes.GetTypeRef("System", "Exception");
                var ctor = new MemberRefUser(
                    module, ".ctor",
                    MethodSig.CreateInstance(module.CorLibTypes.Void),
                    exType);
                body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
                body.Instructions.Add(Instruction.Create(OpCodes.Throw));
                break;
        }

        _nextFail = (FailKind)(((int)_nextFail + 1) % 3);
    }

    private static bool InjectDebuggerCheck(MethodDef method, TypeDef antiDebugType)
    {
        if (!method.HasBody || method.IsPinvokeImpl || method.IsAbstract)
            return false;

        var checkMethod = antiDebugType.FindMethod("Check");
        if (checkMethod == null || method == checkMethod)
            return false;

        var body = method.Body;
        var instructions = body.Instructions;
        if (instructions.Count == 0)
            return false;

        if (instructions.Any(i =>
                i.OpCode == OpCodes.Call && i.Operand is IMethod called &&
                (called == checkMethod || called.Name == "Check" && called.DeclaringType == antiDebugType)))
        {
            return false;
        }

        var insertAt = 0;
        while (insertAt < instructions.Count && instructions[insertAt].OpCode.FlowControl == FlowControl.Meta)
            insertAt++;
        if (insertAt >= instructions.Count)
            return false;

        instructions.Insert(insertAt, Instruction.Create(OpCodes.Call, checkMethod));
        body.UpdateInstructionOffsets();
        return true;
    }

    private MethodDef? FindModuleInitializer(ModuleDef module)
    {
        var globalType = module.GlobalType;
        if (globalType == null)
            return null;

        return globalType.Methods.FirstOrDefault(m =>
            m.IsStaticConstructor ||
            m.Name == ".cctor");
    }

    private static MethodDef CreateModuleInitializer(ModuleDef module)
    {
        var globalType = module.GlobalType;
        if (globalType == null)
        {
            globalType = new TypeDefUser("", "<Module>", null)
            {
                Attributes = TypeAttributes.NotPublic
            };
            module.Types.Insert(0, globalType);
        }

        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        cctor.Body = body;
        globalType.Methods.Add(cctor);
        return cctor;
    }
}
