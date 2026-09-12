using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// XOR-encrypts method IL in the PE image. A module initializer decrypts the IL in memory
/// before JIT using VirtualProtect. Windows-only; generics, helpers, and NativeAOT are skipped
/// or unsupported. Decrypt failures leave ciphertext — they do not restore plaintext IL.
/// </summary>
public class MethodEncryptionObfuscator : IObfuscator
{
    private readonly ILogger<MethodEncryptionObfuscator> _logger;

    public MethodEncryptionObfuscator(ILogger<MethodEncryptionObfuscator> logger)
    {
        _logger = logger;
    }

    public string Name => "MethodEncryption";

    public int Priority => (int)ObfuscationPhase.MethodEncryption;

    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    public bool IsEnabled(ObfySettings settings) =>
        settings.Protection.MethodEncryption && !RuntimeProfileGating.BlocksPeProtections(settings.RuntimeProfile);

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();

        try
        {
            var targets = new List<MethodDef>();
            var genericSkipped = 0;
            var considered = 0;
            foreach (var type in module.GetTypes())
            {
                if (ObfuscatorHelpers.IsRuntimeHelper(type))
                    continue;
                if (type.IsGlobalModuleType)
                    continue;
                if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.All, context.Warnings))
                    continue;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.All, context.Warnings))
                        continue;
                    if (!IsEncryptCandidate(method))
                        continue;

                    considered++;
                    if (method.HasGenericParameters || method.DeclaringType.HasGenericParameters)
                    {
                        genericSkipped++;
                        context.SkippedItems.Add(SkippedItem.GenericMethodSkipped(method.FullName));
                        continue;
                    }

                    targets.Add(method);
                }
            }

            if (genericSkipped > 0 && considered > 0 && genericSkipped * 4 >= considered)
            {
                var warning =
                    $"Method encryption: skipped {genericSkipped} of {considered} candidate methods because they are generic " +
                    $"(not supported). Encrypted {targets.Count}.";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
            }

            if (targets.Count == 0)
            {
                const string unused =
                    "Method encryption was enabled but no eligible methods were encrypted.";
                context.Warnings.Add(unused);
                _logger.LogWarning("{Warning}", unused);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            const string windowsWarning =
                "Method encryption is Windows-only (kernel32!VirtualProtect). Encrypted bodies stay ciphertext if decryption fails; invoking them will crash. Not NativeAOT / IL2CPP.";
            context.Warnings.Add(windowsWarning);
            _logger.LogWarning("{Warning}", windowsWarning);

            var keys = CreateDistinctKeys(targets.Count);
            var decryptor = InjectDecryptor(module, targets.Count);
            var decrypt = decryptor.FindMethod("DecryptBodies")
                ?? throw new InvalidOperationException("Method-encryption decryptor was not injected.");

            var initializer = FindOrCreateModuleInitializer(module);
            initializer.Body!.Instructions.Insert(0, Instruction.Create(OpCodes.Call, decrypt));
            initializer.Body.UpdateInstructionOffsets();

            var entries = new EncryptedMethodBody[targets.Count];
            for (var i = 0; i < targets.Count; i++)
                entries[i] = new EncryptedMethodBody(targets[i], keys[i]);
            context.MethodEncryptionMetadata = new MethodEncryptionMetadata(entries);

            stats.ProtectionsApplied = targets.Count;
            stats.MethodsEncrypted = targets.Count;
            _logger.LogInformation("Prepared {Count} methods for IL encryption", targets.Count);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Method encryption injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Method encryption failed: {ex.Message}", ex));
        }
    }

    private static bool IsEncryptCandidate(MethodDef method)
    {
        if (!method.HasBody || method.Body.Instructions.Count < 2)
            return false;
        if (method.IsAbstract || method.IsPinvokeImpl || method.IsNative || method.IsUnmanaged)
            return false;
        if (method.IsStaticConstructor)
            return false;
        return true;
    }

    private static byte[] CreateDistinctKeys(int count)
    {
        var keys = new byte[count];
        var used = new HashSet<byte>();
        for (var i = 0; i < count; i++)
        {
            byte key;
            do
            {
                key = (byte)Random.Shared.Next(1, 256);
            } while (used.Count < 255 && !used.Add(key));
            keys[i] = key;
        }

        return keys;
    }

    private static TypeDef InjectDecryptor(ModuleDef module, int methodCount)
    {
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<MethodCrypt>",
            module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };

        var blobType = new TypeDefUser("Obfy.Runtime", "<MethodCryptBlob>",
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout |
                         TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
        };
        var blobSize = MethodEncryptionMetadata.BlobSize(methodCount);
        blobType.ClassLayout = new ClassLayoutUser(1, (uint)blobSize);
        typeDef.NestedTypes.Add(blobType);

        var blob = new byte[blobSize];
        MethodEncryptionMetadata.Magic.CopyTo(blob.AsSpan(0, MethodEncryptionMetadata.MagicLength));

        var blobField = new FieldDefUser(
            "_blob",
            new FieldSig(blobType.ToTypeSig()),
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.HasFieldRVA)
        {
            InitialValue = blob
        };
        typeDef.Fields.Add(blobField);

        var virtualProtect = CreateVirtualProtect(module);
        typeDef.Methods.Add(virtualProtect);
        typeDef.Methods.Add(CreateDecryptBodies(module, typeDef, blobField, virtualProtect));
        module.Types.Add(typeDef);
        return typeDef;
    }

    private static MethodDef CreateVirtualProtect(ModuleDef module)
    {
        var uint32 = module.CorLibTypes.UInt32;
        return new MethodDefUser(
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
    }

    private static MethodDef CreateDecryptBodies(
        ModuleDef module,
        TypeDef declaringType,
        FieldDef blobField,
        MethodDef virtualProtect)
    {
        var method = new MethodDefUser(
            "DecryptBodies",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var marshalType = new TypeRefUser(module, "System.Runtime.InteropServices", "Marshal", module.CorLibTypes.AssemblyRef);
        var moduleType = new TypeRefUser(module, "System.Reflection", "Module", module.CorLibTypes.AssemblyRef);
        var typeType = new TypeRefUser(module, "System", "Type", module.CorLibTypes.AssemblyRef);
        var runtimeTypeHandle = new TypeRefUser(module, "System", "RuntimeTypeHandle", module.CorLibTypes.AssemblyRef);
        var intPtrType = new TypeRefUser(module, "System", "IntPtr", module.CorLibTypes.AssemblyRef);

        var getTypeFromHandle = new MemberRefUser(module, "GetTypeFromHandle",
            MethodSig.CreateStatic(new ClassSig(typeType), new ValueTypeSig(runtimeTypeHandle)), typeType);
        var getModule = new MemberRefUser(module, "get_Module",
            MethodSig.CreateInstance(new ClassSig(moduleType)), typeType);
        var getHinstance = new MemberRefUser(module, "GetHINSTANCE",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, new ClassSig(moduleType)), marshalType);
        var intPtrAdd = new MemberRefUser(module, "Add",
            MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32), intPtrType);
        var readInt32 = new MemberRefUser(module, "ReadInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32), marshalType);
        var readByte = new MemberRefUser(module, "ReadByte",
            MethodSig.CreateStatic(module.CorLibTypes.Byte, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32), marshalType);
        var writeByte = new MemberRefUser(module, "WriteByte",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.IntPtr, module.CorLibTypes.Int32, module.CorLibTypes.Byte), marshalType);
        var env = module.CorLibTypes.GetTypeRef("System", "Environment");
        var failFast = new MemberRefUser(module, "FailFast",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.String), env);
        var dllNotFound = module.CorLibTypes.GetTypeRef("System", "DllNotFoundException");
        var entryNotFound = module.CorLibTypes.GetTypeRef("System", "EntryPointNotFoundException");

        var addr = new Local(module.CorLibTypes.IntPtr);
        var blob = new Local(module.CorLibTypes.IntPtr);
        var i = new Local(module.CorLibTypes.Int32);
        var count = new Local(module.CorLibTypes.Int32);
        var key = new Local(module.CorLibTypes.Int32);
        var rva = new Local(module.CorLibTypes.Int32);
        var header = new Local(module.CorLibTypes.Int32);
        var size = new Local(module.CorLibTypes.Int32);
        var j = new Local(module.CorLibTypes.Int32);
        var oldProtect = new Local(module.CorLibTypes.UInt32);
        var entryOff = new Local(module.CorLibTypes.Int32);
        var byteOff = new Local(module.CorLibTypes.Int32);
        var xorByte = new Local(module.CorLibTypes.Int32);
        foreach (var local in new[] { addr, blob, i, count, key, rva, header, size, j, oldProtect, entryOff, byteOff, xorByte })
            body.Variables.Add(local);

        var tryStart = Instruction.Create(OpCodes.Ldtoken, declaringType);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchDll = Instruction.Create(OpCodes.Pop);
        var catchEntry = Instruction.Create(OpCodes.Pop);
        var leaveEnd = Instruction.Create(OpCodes.Leave, ret);
        var loopCheck = Instruction.Create(OpCodes.Ldloc, i);
        var innerCheck = Instruction.Create(OpCodes.Ldloc, j);
        var next = Instruction.Create(OpCodes.Ldloc, i);

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getModule));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getHinstance));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, leaveEnd));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldsflda, blobField));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, blob));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, blob));
        body.Instructions.Add(Instruction.CreateLdcI4(MethodEncryptionMetadata.MagicLength));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, count));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, i));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loopCheck));

        var loopBody = Instruction.Create(OpCodes.Ldloc, i);
        body.Instructions.Add(loopBody);
        body.Instructions.Add(Instruction.CreateLdcI4(MethodEncryptionMetadata.EntryBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        body.Instructions.Add(Instruction.CreateLdcI4(
            MethodEncryptionMetadata.MagicLength + MethodEncryptionMetadata.HeaderBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, entryOff));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, blob));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, entryOff));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, rva));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, blob));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, entryOff));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_4));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, header));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, blob));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, entryOff));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_8));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, size));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, blob));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, entryOff));
        body.Instructions.Add(Instruction.CreateLdcI4(MethodEncryptionMetadata.KeyOffset));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, key));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, rva));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, next));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, size));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, next));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, rva));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, intPtrAdd));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, header));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, size));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U4));
        body.Instructions.Add(Instruction.CreateLdcI4(0x40));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, oldProtect));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, virtualProtect));
        var xorStart = Instruction.Create(OpCodes.Ldc_I4_0);
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, xorStart));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ""));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, failFast));

        body.Instructions.Add(xorStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, j));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, innerCheck));

        var innerBody = Instruction.Create(OpCodes.Ldloc, rva);
        body.Instructions.Add(innerBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, header));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, j));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, byteOff));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, byteOff));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readByte));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, key));
        body.Instructions.Add(Instruction.Create(OpCodes.Xor));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, xorByte));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, addr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, byteOff));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, xorByte));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, writeByte));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, j));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, j));

        body.Instructions.Add(innerCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, size));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, innerBody));

        body.Instructions.Add(next);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, i));

        body.Instructions.Add(loopCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, count));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loopBody));

        body.Instructions.Add(leaveEnd);
        body.Instructions.Add(catchDll);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, ret));
        body.Instructions.Add(catchEntry);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, ret));
        body.Instructions.Add(ret);

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
            HandlerEnd = ret,
            CatchType = entryNotFound
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
