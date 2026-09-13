using System.Runtime.InteropServices;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects an in-memory PE-header wipe at module load and, on Windows X86/X64, overwrites the
/// first byte of in-process <c>dbghelp!MiniDumpWriteDump</c> with <c>0xC3</c> (x86/x64 <c>ret</c>).
/// There is no ARM64 encoding; non-X86/X64 processes skip the write. <c>VirtualProtect</c> failure
/// skips the write. Missing <c>kernel32</c>/<c>dbghelp</c> and non-Windows throws are caught in
/// <c>Wipe</c> so the app still starts. The dump hook always runs after the PE wipe attempt.
/// External dumpers (ProcDump, Task Manager, other processes' <c>MiniDumpWriteDump</c>),
/// <c>dbgcore</c>, and raw <c>ReadProcessMemory</c> are unaffected.
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

    public bool IsEnabled(ObfySettings settings) =>
        settings.Protection.AntiDump && RuntimeProfileGating.AllowsPeMutation(settings.RuntimeProfile);

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();

        try
        {
            var antiDumpType = InjectAntiDumpType(module);
            RuntimeInjection.Register(context, antiDumpType);
            var wipe = antiDumpType.FindMethod("Wipe")
                ?? throw new InvalidOperationException("Anti-dump wipe method was not injected.");

            RuntimeInjection.PrependModuleInitializerCall(module, wipe);
            stats.ProtectionsApplied++;

            const string windowsWarning =
                "Anti-dump is Windows-only (kernel32 VirtualProtect / dbghelp MiniDumpWriteDump). " +
                "The MiniDumpWriteDump patch writes 0xC3 (x86/x64 ret) in this process after an X86/X64 " +
                "architecture check; ARM64 is skipped. VirtualProtect failure skips the write. " +
                "External dumpers (ProcDump, Task Manager) are unaffected. " +
                "PE-wipe failures are swallowed at runtime.";
            context.Warnings.Add(windowsWarning);
            _logger.LogWarning("{Warning}", windowsWarning);

            _logger.LogInformation("Injected anti-dump protection");
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        var getModuleHandle = CreateKernel32PInvoke(module, "GetModuleHandleW",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.String),
            PInvokeAttributes.CharSetUnicode);
        var loadLibrary = CreateKernel32PInvoke(module, "LoadLibraryW",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.String),
            PInvokeAttributes.CharSetUnicode);
        var getProc = CreateKernel32PInvoke(module, "GetProcAddress",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.String),
            PInvokeAttributes.CharSetAnsi);
        typeDef.Methods.Add(getModuleHandle);
        typeDef.Methods.Add(loadLibrary);
        typeDef.Methods.Add(getProc);
        var neutralize = CreateNeutralizeDumpers(module, virtualProtect, getModuleHandle, loadLibrary, getProc);
        typeDef.Methods.Add(neutralize);
        typeDef.Methods.Add(CreateWipeMethod(module, typeDef, virtualProtect, neutralize));
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

    private static MethodDef CreateKernel32PInvoke(
        ModuleDef module, string name, MethodSig sig, PInvokeAttributes charSet)
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
                PInvokeAttributes.SupportsLastError | PInvokeAttributes.CallConvWinapi | PInvokeAttributes.NoMangle | charSet)
        };
    }

    private static MethodDef CreateNeutralizeDumpers(
        ModuleDef module,
        MethodDef virtualProtect,
        MethodDef getModuleHandle,
        MethodDef loadLibrary,
        MethodDef getProcAddress)
    {
        var method = new MethodDefUser(
            "NeutralizeDumpers",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var architectureType = new TypeRefUser(module, "System.Runtime.InteropServices", "Architecture", module.CorLibTypes.AssemblyRef);
        var runtimeInformationType = new TypeRefUser(module, "System.Runtime.InteropServices", "RuntimeInformation", module.CorLibTypes.AssemblyRef);
        var getProcessArchitecture = new MemberRefUser(module, "get_ProcessArchitecture",
            MethodSig.CreateStatic(new ValueTypeSig(architectureType)), runtimeInformationType);

        var archLocal = new Local(new ValueTypeSig(architectureType));
        var moduleLocal = new Local(module.CorLibTypes.IntPtr);
        var procLocal = new Local(module.CorLibTypes.IntPtr);
        var oldProtect = new Local(module.CorLibTypes.UInt32);
        body.Variables.Add(archLocal);
        body.Variables.Add(moduleLocal);
        body.Variables.Add(procLocal);
        body.Variables.Add(oldProtect);

        var marshalType = new TypeRefUser(module, "System.Runtime.InteropServices", "Marshal", module.CorLibTypes.AssemblyRef);
        var writeByte = new MemberRefUser(module, "WriteByte",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.IntPtr, module.CorLibTypes.Byte),
            marshalType);

        // Always GetModuleHandle then LoadLibrary. If MiniDumpWriteDump is found, VirtualProtect
        // must succeed before writing 0xC3. ARM64 ret is 0xD65F03C0 — skipped via ProcessArchitecture.
        // VirtualProtect is not restored.
        var ret = Instruction.Create(OpCodes.Ret);
        var haveModule = Instruction.Create(OpCodes.Stloc, moduleLocal);
        var loadDbghelp = Instruction.Create(OpCodes.Ldstr, "dbghelp.dll");

        body.Instructions.Add(Instruction.Create(OpCodes.Call, getProcessArchitecture));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, archLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, archLocal));
        body.Instructions.Add(Instruction.CreateLdcI4((int)Architecture.X86));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, loadDbghelp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, archLocal));
        body.Instructions.Add(Instruction.CreateLdcI4((int)Architecture.X64));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, ret));

        body.Instructions.Add(loadDbghelp);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getModuleHandle));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, haveModule));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "dbghelp.dll"));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, loadLibrary));
        body.Instructions.Add(haveModule);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, moduleLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, ret));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, moduleLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "MiniDumpWriteDump"));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getProcAddress));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, procLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, ret));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(Instruction.CreateLdcI4(0x40));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, ret));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0xC3));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeByte));
        body.Instructions.Add(ret);
        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }

    private static MethodDef CreateWipeMethod(ModuleDef module, TypeDef declaringType, MethodDef virtualProtect, MethodDef neutralize)
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
        var magicLocal = new Local(module.CorLibTypes.Int32);
        var dirBaseLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(addrLocal);
        body.Variables.Add(peLocal);
        body.Variables.Add(oldProtect);
        body.Variables.Add(magicLocal);
        body.Variables.Add(dirBaseLocal);

        var tryStart = Instruction.Create(OpCodes.Ldtoken, declaringType);
        var afterTry = Instruction.Create(OpCodes.Call, neutralize);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var leaveEnd = Instruction.Create(OpCodes.Leave, afterTry);

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getModule));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getHinstance));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, leaveEnd));

        // Dynamic/emit modules report HINSTANCE -1; skip PE writes, still neutralize dumpers.
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, leaveEnd));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0x1000));
        body.Instructions.Add(Instruction.CreateLdcI4(0x40));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, leaveEnd));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0x3C));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, peLocal));

        // Zero DOS MZ signature (dumpers look for it)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeInt32));

        // Skip optional-header writes when e_lfanew is outside the 0x1000 VirtualProtect window.
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, peLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(64));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, leaveEnd));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, peLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0xC00));
        body.Instructions.Add(Instruction.Create(OpCodes.Bgt, leaveEnd));

        // Magic at optional+0 (e_lfanew+24). 0x20B = PE32+, otherwise PE32 data-directory layout.
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addrLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, peLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(24));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.CreateLdcI4(0xFFFF));
        body.Instructions.Add(Instruction.Create(OpCodes.And));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, magicLocal));

        body.Instructions.Add(Instruction.CreateLdcI4(96)); // PE32 data directories
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, dirBaseLocal));
        var usePe32Dirs = Instruction.Create(OpCodes.Nop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, magicLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0x20B));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, usePe32Dirs));
        body.Instructions.Add(Instruction.CreateLdcI4(112)); // PE32+ data directories
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, dirBaseLocal));
        body.Instructions.Add(usePe32Dirs);

        // Optional-header CheckSum is at +64 for both PE32 and PE32+.
        EmitWriteZero(body, addrLocal, peLocal, 24 + 64, writeInt32);

        // Import directory RVA/Size (data directory 1)
        EmitWriteZeroAtDir(body, addrLocal, peLocal, dirBaseLocal, 8, writeInt32);
        EmitWriteZeroAtDir(body, addrLocal, peLocal, dirBaseLocal, 12, writeInt32);

        // Debug directory RVA (data directory 6)
        EmitWriteZeroAtDir(body, addrLocal, peLocal, dirBaseLocal, 48, writeInt32);

        // IAT RVA (data directory 12)
        EmitWriteZeroAtDir(body, addrLocal, peLocal, dirBaseLocal, 96, writeInt32);
        body.Instructions.Add(leaveEnd);

        body.Instructions.Add(catchPop);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, afterTry));
        body.Instructions.Add(afterTry);
        body.Instructions.Add(ret);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = afterTry,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });

        body.UpdateInstructionOffsets();
        return method;
    }

    private static void EmitWriteZero(
        CilBody body, Local addr, Local pe, int offsetFromPe, MemberRef writeInt32)
    {
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pe));
        body.Instructions.Add(Instruction.CreateLdcI4(offsetFromPe));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeInt32));
    }

    private static void EmitWriteZeroAtDir(
        CilBody body, Local addr, Local pe, Local dirBase, int dirOffset, MemberRef writeInt32)
    {
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pe));
        body.Instructions.Add(Instruction.CreateLdcI4(24));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, dirBase));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.CreateLdcI4(dirOffset));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeInt32));
    }

}
