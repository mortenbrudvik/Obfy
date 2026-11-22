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
/// start of in-process <c>dbghelp!MiniDumpWriteDump</c> with <c>31 C0 C3</c> (<c>xor eax, eax; ret</c>)
/// on x64 or <c>31 C0 C2 1C 00</c> (<c>xor eax, eax; ret 0x1C</c>) on x86, then restores page protection.
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
                "The MiniDumpWriteDump patch writes xor eax,eax + ret (0x31 0xC0 0xC3 on x64; " +
                "0x31 0xC0 0xC2 0x1C 0x00 on x86) in this process after an X86/X64 architecture check; " +
                "ARM64 is skipped. VirtualProtect failure skips the write. " +
                "External dumpers (ProcDump, Task Manager) are unaffected. " +
                "PE-wipe failures are swallowed at runtime.";
            context.Warnings.Add(windowsWarning);
            _logger.LogWarning("{Warning}", windowsWarning);

            _logger.LogInformation("Injected anti-dump protection");
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Anti-dump injection failed ({ExceptionType})", ex.GetType().Name);
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
        var loadLibraryEx = CreateKernel32PInvoke(module, "LoadLibraryExW",
            MethodSig.CreateStatic(
                module.CorLibTypes.IntPtr,
                module.CorLibTypes.String,
                module.CorLibTypes.IntPtr,
                module.CorLibTypes.UInt32),
            PInvokeAttributes.CharSetUnicode);
        var getProc = CreateKernel32PInvoke(module, "GetProcAddress",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.String),
            PInvokeAttributes.CharSetAnsi);
        typeDef.Methods.Add(getModuleHandle);
        typeDef.Methods.Add(loadLibraryEx);
        typeDef.Methods.Add(getProc);
        var neutralize = CreateNeutralizeDumpers(module, virtualProtect, getModuleHandle, loadLibraryEx, getProc);
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
                module.CorLibTypes.UIntPtr,
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
        MethodDef loadLibraryEx,
        MethodDef getProcAddress)
    {
        const int loadLibrarySearchSystem32 = 0x00000800;

        var method = new MethodDefUser(
            "NeutralizeDumpers",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var supportsRuntimeInfo = FrameworkReferences.SupportsRuntimeInformation(module);
        TypeSig archSig;
        MemberRefUser getProcessArchitecture;
        int x86;
        int x64;
        if (supportsRuntimeInfo)
        {
            var architectureType = FrameworkReferences.Type(module, "System.Runtime.InteropServices", "Architecture");
            var runtimeInformationType = FrameworkReferences.Type(module, "System.Runtime.InteropServices", "RuntimeInformation");
            archSig = new ValueTypeSig(architectureType);
            getProcessArchitecture = new MemberRefUser(module, "get_ProcessArchitecture",
                MethodSig.CreateStatic(archSig), runtimeInformationType);
            x86 = (int)Architecture.X86;
            x64 = (int)Architecture.X64;
        }
        else
        {
            // .NET Framework before 4.7.1 has no RuntimeInformation. Reject ARM/ARM64 hosts via
            // PROCESSOR_ARCHITECTURE, then distinguish x86 from x64 by pointer size.
            archSig = module.CorLibTypes.Int32;
            getProcessArchitecture = new MemberRefUser(module, "get_Size",
                MethodSig.CreateStatic(module.CorLibTypes.Int32), FrameworkReferences.Type(module, "System", "IntPtr"));
            x86 = 4;
            x64 = 8;
        }

        var archLocal = new Local(archSig);
        var isX86Local = new Local(module.CorLibTypes.Boolean);
        var sizeLocal = new Local(module.CorLibTypes.Int32);
        var moduleLocal = new Local(module.CorLibTypes.IntPtr);
        var procLocal = new Local(module.CorLibTypes.IntPtr);
        var oldProtect = new Local(module.CorLibTypes.UInt32);
        var restoredProtect = new Local(module.CorLibTypes.UInt32);
        body.Variables.Add(archLocal);
        body.Variables.Add(isX86Local);
        body.Variables.Add(sizeLocal);
        body.Variables.Add(moduleLocal);
        body.Variables.Add(procLocal);
        body.Variables.Add(oldProtect);
        body.Variables.Add(restoredProtect);

        var marshalType = FrameworkReferences.Type(module, "System.Runtime.InteropServices", "Marshal");
        var writeByte = new MemberRefUser(module, "WriteByte",
            MethodSig.CreateStatic(
                module.CorLibTypes.Void,
                module.CorLibTypes.IntPtr,
                module.CorLibTypes.Int32,
                module.CorLibTypes.Byte),
            marshalType);

        var ret = Instruction.Create(OpCodes.Ret);
        var setX86 = Instruction.Create(OpCodes.Ldc_I4_1);
        var setX64 = Instruction.Create(OpCodes.Ldc_I4_0);
        var loadDbghelp = Instruction.Create(OpCodes.Ldstr, "dbghelp.dll");
        var haveModule = Instruction.Create(OpCodes.Stloc, moduleLocal);
        var patchX86 = Instruction.Create(OpCodes.Ldloc, procLocal);
        var restoreProtect = Instruction.Create(OpCodes.Ldloc, procLocal);

        if (!supportsRuntimeInfo)
        {
            var procArchLocal = new Local(module.CorLibTypes.String);
            body.Variables.Add(procArchLocal);
            var envType = FrameworkReferences.Type(module, "System", "Environment");
            var stringType = FrameworkReferences.Type(module, "System", "String");
            var stringComparisonType = FrameworkReferences.Type(module, "System", "StringComparison");
            var getEnvVar = new MemberRefUser(module, "GetEnvironmentVariable",
                MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String), envType);
            var indexOf = new MemberRefUser(module, "IndexOf",
                MethodSig.CreateInstance(
                    module.CorLibTypes.Int32,
                    module.CorLibTypes.String,
                    new ValueTypeSig(stringComparisonType)),
                stringType);

            var checkPtrSize = Instruction.Create(OpCodes.Call, getProcessArchitecture);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "PROCESSOR_ARCHITECTURE"));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getEnvVar));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, procArchLocal));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procArchLocal));
            body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, checkPtrSize));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procArchLocal));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "ARM"));
            body.Instructions.Add(Instruction.CreateLdcI4((int)StringComparison.OrdinalIgnoreCase));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, indexOf));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Bge, ret));
            body.Instructions.Add(checkPtrSize);
        }
        else
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getProcessArchitecture));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, archLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, archLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(x86));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, setX86));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, archLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(x64));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, setX64));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, ret));

        body.Instructions.Add(setX86);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, isX86Local));
        body.Instructions.Add(Instruction.CreateLdcI4(5));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loadDbghelp));

        body.Instructions.Add(setX64);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, isX86Local));
        body.Instructions.Add(Instruction.CreateLdcI4(3));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sizeLocal));

        body.Instructions.Add(loadDbghelp);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getModuleHandle));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, haveModule));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "dbghelp.dll"));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.CreateLdcI4(loadLibrarySearchSystem32));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, loadLibraryEx));
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
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U));
        body.Instructions.Add(Instruction.CreateLdcI4(0x40));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, ret));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, isX86Local));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, patchX86));

        // x64: xor eax, eax; ret (31 C0 C3)
        EmitPatchByte(body, procLocal, 0, 0x31, writeByte);
        EmitPatchByte(body, procLocal, 1, 0xC0, writeByte);
        EmitPatchByte(body, procLocal, 2, 0xC3, writeByte);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, restoreProtect));

        // x86 (__stdcall 7 args = 28 bytes): xor eax, eax; ret 0x1C (31 C0 C2 1C 00)
        body.Instructions.Add(patchX86);
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.CreateLdcI4(0x31));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeByte));
        EmitPatchByte(body, procLocal, 1, 0xC0, writeByte);
        EmitPatchByte(body, procLocal, 2, 0xC2, writeByte);
        EmitPatchByte(body, procLocal, 3, 0x1C, writeByte);
        EmitPatchByte(body, procLocal, 4, 0x00, writeByte);

        // Restore original page protection so pages do not remain RWX.
        body.Instructions.Add(restoreProtect);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, restoredProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(ret);
        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }

    private static void EmitPatchByte(CilBody body, Local procLocal, int offset, int value, MemberRef writeByte)
    {
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, procLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(offset));
        body.Instructions.Add(Instruction.CreateLdcI4(value));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeByte));
    }

    private static MethodDef CreateWipeMethod(ModuleDef module, TypeDef declaringType, MethodDef virtualProtect, MethodDef neutralize)
    {
        var method = new MethodDefUser(
            "Wipe",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var marshalType = FrameworkReferences.Type(module, "System.Runtime.InteropServices", "Marshal");
        var moduleType = FrameworkReferences.Type(module, "System.Reflection", "Module");
        var typeType = FrameworkReferences.Type(module, "System", "Type");
        var runtimeTypeHandle = FrameworkReferences.Type(module, "System", "RuntimeTypeHandle");

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
        var neutralizeCall = Instruction.Create(OpCodes.Call, neutralize);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var leaveEnd = Instruction.Create(OpCodes.Leave, neutralizeCall);
        var catchNeutralize = Instruction.Create(OpCodes.Pop);

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
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U));
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
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, neutralizeCall));
        // NeutralizeDumpers loads dbghelp and RuntimeInformation. A missing library or
        // architecture helper must not fail the module initializer. The call still runs
        // after the PE wipe, including when that wipe leaves early.
        body.Instructions.Add(neutralizeCall);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, ret));
        body.Instructions.Add(catchNeutralize);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, ret));
        body.Instructions.Add(ret);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = neutralizeCall,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = neutralizeCall,
            TryEnd = catchNeutralize,
            HandlerStart = catchNeutralize,
            HandlerEnd = ret,
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
