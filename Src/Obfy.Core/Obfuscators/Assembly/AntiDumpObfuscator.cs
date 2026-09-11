using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects an in-memory PE-header wipe that runs at module load, breaking common dumpers.
/// Failures (non-Windows, missing kernel32) are swallowed so the app still starts.
/// </summary>
public class AntiDumpObfuscator : IObfuscator
{
    private readonly ILogger<AntiDumpObfuscator> _logger;

    public AntiDumpObfuscator(ILogger<AntiDumpObfuscator> logger)
    {
        _logger = logger;
    }

    public string Name => "AntiDump";

    public int Priority => (int)ObfuscationPhase.AntiDump;

    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiDump;

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();

        try
        {
            var antiDumpType = InjectAntiDumpType(module);
            var wipe = antiDumpType.FindMethod("Wipe")
                ?? throw new InvalidOperationException("Anti-dump wipe method was not injected.");

            var initializer = FindOrCreateModuleInitializer(module);
            initializer.Body!.Instructions.Insert(0, Instruction.Create(OpCodes.Call, wipe));
            initializer.Body.UpdateInstructionOffsets();
            stats.ProtectionsApplied++;

            _logger.LogInformation("Injected anti-dump protection");
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anti-dump injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Anti-dump injection failed: {ex.Message}", ex));
        }
    }

    private static TypeDef InjectAntiDumpType(ModuleDef module)
    {
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<AntiDump>",
            module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };

        var virtualProtect = CreateVirtualProtect(module);
        typeDef.Methods.Add(virtualProtect);
        typeDef.Methods.Add(CreateWipeMethod(module, typeDef, virtualProtect));
        module.Types.Add(typeDef);
        return typeDef;
    }

    private static MethodDef CreateVirtualProtect(ModuleDef module)
    {
        var uint32 = module.CorLibTypes.UInt32;
        var method = new MethodDefUser(
            "VirtualProtect",
            MethodSig.CreateStatic(
                module.CorLibTypes.Boolean,
                module.CorLibTypes.IntPtr,
                uint32,
                uint32,
                new ByRefSig(uint32)),
            MethodImplAttributes.PreserveSig,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl)
        {
            ImplMap = new ImplMapUser(
                new ModuleRefUser(module, "kernel32"),
                "VirtualProtect",
                PInvokeAttributes.SupportsLastError | PInvokeAttributes.CallConvWinapi | PInvokeAttributes.NoMangle)
        };
        return method;
    }

    private static MethodDef CreateWipeMethod(ModuleDef module, TypeDef declaringType, MethodDef virtualProtect)
    {
        var method = new MethodDefUser(
            "Wipe",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var marshalType = new TypeRefUser(module, "System.Runtime.InteropServices", "Marshal", module.CorLibTypes.AssemblyRef);
        var moduleType = new TypeRefUser(module, "System.Reflection", "Module", module.CorLibTypes.AssemblyRef);
        var typeType = new TypeRefUser(module, "System", "Type", module.CorLibTypes.AssemblyRef);
        var runtimeTypeHandle = new TypeRefUser(module, "System", "RuntimeTypeHandle", module.CorLibTypes.AssemblyRef);

        var getTypeFromHandle = new MemberRefUser(module, "GetTypeFromHandle",
            MethodSig.CreateStatic(new ClassSig(typeType), new ValueTypeSig(runtimeTypeHandle)),
            typeType);
        var getModule = new MemberRefUser(module, "get_Module",
            MethodSig.CreateInstance(new ClassSig(moduleType)), typeType);
        var getHinstance = new MemberRefUser(module, "GetHINSTANCE",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, new ClassSig(moduleType)), marshalType);
        var readInt32 = new MemberRefUser(module, "ReadInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32), marshalType);
        var writeInt32 = new MemberRefUser(module, "WriteInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32, module.CorLibTypes.Int32), marshalType);

        var addrLocal = new Local(module.CorLibTypes.IntPtr);
        var peLocal = new Local(module.CorLibTypes.Int32);
        var oldProtect = new Local(module.CorLibTypes.UInt32);
        body.Variables.Add(addrLocal);
        body.Variables.Add(peLocal);
        body.Variables.Add(oldProtect);

        var tryStart = Instruction.Create(OpCodes.Ldtoken, declaringType);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var afterAddr = Instruction.Create(OpCodes.Ldloc, addrLocal);
        var leaveEnd = Instruction.Create(OpCodes.Leave, ret);

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getModule));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getHinstance));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, leaveEnd));

        body.Instructions.Add(afterAddr);
        body.Instructions.Add(Instruction.CreateLdcI4(0x1000));
        body.Instructions.Add(Instruction.CreateLdcI4(0x40));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0x3C));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, peLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, peLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(24 + 64));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeInt32));
        body.Instructions.Add(leaveEnd);

        body.Instructions.Add(catchPop);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, ret));
        body.Instructions.Add(ret);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = ret,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });

        body.UpdateInstructionOffsets();
        return method;
    }

    private static MethodDef FindOrCreateModuleInitializer(ModuleDef module)
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

        var cctor = globalType.Methods.FirstOrDefault(m => m.IsStaticConstructor || m.Name == ".cctor");
        if (cctor != null)
            return cctor;

        cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        cctor.Body = body;
        globalType.Methods.Add(cctor);
        return cctor;
    }
}
