using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects anti-tampering protection into assemblies.
/// Stores a SHA-256 of the PE image (hash slot zeroed) and verifies it at runtime.
/// </summary>
public class AntiTamperObfuscator : IObfuscator
{
    private readonly ILogger<AntiTamperObfuscator> _logger;

    public AntiTamperObfuscator(ILogger<AntiTamperObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "AntiTamper";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.AntiTamper;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiTamper.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.Protection.AntiTamper;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting anti-tamper protection injection");

        try
        {
            var antiTamperType = InjectAntiTamperType(module);

            context.AntiTamperMetadata = AntiTamperMetadata.Injected;

            // Add check to entry point if configured
            if (settings.CheckEntryPoint && module.EntryPoint != null &&
                InjectVerificationCall(module.EntryPoint, antiTamperType))
            {
                stats.ProtectionsApplied++;
                _logger.LogDebug("Injected anti-tamper check at entry point");
            }

            // Add check to module initializer if configured
            if (settings.CheckModuleInitializer)
            {
                var moduleInitializer = ObfuscatorHelpers.FindOrCreateModuleInitializer(module);
                if (moduleInitializer != null && InjectVerificationCall(moduleInitializer, antiTamperType))
                {
                    stats.ProtectionsApplied++;
                    _logger.LogDebug("Injected anti-tamper check at module initializer");
                }
            }

            if (stats.ProtectionsApplied == 0)
            {
                const string noneInjected =
                    "Anti-tamper is enabled but no verification call was injected (no entry point and no module initializer).";
                _logger.LogError("{Message}", noneInjected);
                return Task.FromResult(ObfuscationResult.Failed(noneInjected));
            }

            _logger.LogInformation("Applied {Count} anti-tamper protections", stats.ProtectionsApplied);

            // Emitted unconditionally: at obfuscation time we cannot know whether the consumer will
            // publish as single-file. The runtime still skips when Assembly.Location is empty
            // (single-file) or for packed ALC/LoadFromStream loads (hashing the launcher would false-fail).
            const string singleFileWarning =
                "Anti-tamper: the integrity check verifies the assembly file on disk and is skipped for " +
                "single-file / self-contained deployments (Assembly.Location is empty) and packed ALC/" +
                "LoadFromStream loads. Ship a file-based, unpacked deployment for tamper protection to take effect.";
            context.Warnings.Add(singleFileWarning);
            _logger.LogWarning("{Warning}", singleFileWarning);

            if (context.Settings.Packing.Enabled)
            {
                const string packingWarning =
                    "Anti-tamper is skipped at runtime for packed ALC/LoadFromStream loads. " +
                    "The launcher will not verify the embedded payload.";
                context.Warnings.Add(packingWarning);
                _logger.LogWarning("{Warning}", packingWarning);
            }

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Anti-tamper injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Anti-tamper injection failed: {ex.Message}", ex));
        }
    }

    private TypeDef InjectAntiTamperType(ModuleDef module)
    {
        // Create internal static class for anti-tamper
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<AntiTamper>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        var blobType = new TypeDefUser("Obfy.Runtime", "<AntiTamperBlob>",
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef));
        blobType.Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
        blobType.ClassLayout = new ClassLayoutUser(1, (uint)AssemblyHashComputer.BlobSize);
        typeDef.NestedTypes.Add(blobType);

        var blob = new byte[AssemblyHashComputer.BlobSize];
        Buffer.BlockCopy(AssemblyHashComputer.Magic, 0, blob, 0, AssemblyHashComputer.Magic.Length);

        var blobField = new FieldDefUser(
            "_blob",
            new FieldSig(blobType.ToTypeSig()),
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.HasFieldRVA)
        {
            InitialValue = blob
        };
        typeDef.Fields.Add(blobField);

        var verifiedField = new FieldDefUser(
            "_v",
            new FieldSig(module.CorLibTypes.Boolean),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(verifiedField);

        var findOffset = CreateFindHashOffsetMethod(module);
        typeDef.Methods.Add(findOffset);

        var verifyMethod = CreateVerifyMethod(module, verifiedField, findOffset);
        typeDef.Methods.Add(verifyMethod);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private static MethodDef CreateFindHashOffsetMethod(ModuleDef module)
    {
        var method = new MethodDefUser(
            "FindHashOffset",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Byte)),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var magicLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var iLocal = new Local(module.CorLibTypes.Int32);
        var jLocal = new Local(module.CorLibTypes.Int32);
        var maxLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(magicLocal);
        body.Variables.Add(iLocal);
        body.Variables.Add(jLocal);
        body.Variables.Add(maxLocal);

        var magic = AssemblyHashComputer.Magic;
        body.Instructions.Add(Instruction.CreateLdcI4(magic.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        for (var m = 0; m < magic.Length; m++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(m));
            body.Instructions.Add(Instruction.CreateLdcI4(magic[m]));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, magicLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.BlobSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, maxLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        var iCheck = Instruction.Create(OpCodes.Ldloc, iLocal);
        var iBody = Instruction.Create(OpCodes.Ldc_I4_0);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, iCheck));

        body.Instructions.Add(iBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, jLocal));
        var jCheck = Instruction.Create(OpCodes.Ldloc, jLocal);
        var jBody = Instruction.Create(OpCodes.Ldarg_0);
        var mismatch = Instruction.Create(OpCodes.Ldloc, iLocal);
        var match = Instruction.Create(OpCodes.Ldloc, iLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, jCheck));

        body.Instructions.Add(jBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, jLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, magicLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, jLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, mismatch));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, jLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, jLocal));

        body.Instructions.Add(jCheck);
        body.Instructions.Add(Instruction.CreateLdcI4(magic.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, jBody));
        body.Instructions.Add(match);
        body.Instructions.Add(Instruction.CreateLdcI4(magic.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.Instructions.Add(mismatch);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        body.Instructions.Add(iCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, maxLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ble, iBody));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    private static MethodDef CreateVerifyMethod(ModuleDef module, FieldDef verifiedField, MethodDef findOffset)
    {
        var method = new MethodDefUser(
            "Verify",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var pathLocal = new Local(module.CorLibTypes.String);
        var bytesLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var storedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var computedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var offsetLocal = new Local(module.CorLibTypes.Int32);
        var iLocal = new Local(module.CorLibTypes.Int32);
        var snOffLocal = new Local(module.CorLibTypes.Int32);
        var snSizeLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(pathLocal);
        body.Variables.Add(bytesLocal);
        body.Variables.Add(storedLocal);
        body.Variables.Add(computedLocal);
        body.Variables.Add(offsetLocal);
        body.Variables.Add(iLocal);
        body.Variables.Add(snOffLocal);
        body.Variables.Add(snSizeLocal);

        var assemblyType = new TypeRefUser(module, "System.Reflection", "Assembly", module.CorLibTypes.AssemblyRef);
        var environmentType = module.CorLibTypes.GetTypeRef("System", "Environment");
        var fileType = new TypeRefUser(module, "System.IO", "File", module.CorLibTypes.AssemblyRef);
        // SHA256 lives in the cryptography assembly, not the corlib facade, on modern .NET.
        var sha256Type = new TypeRefUser(module, "System.Security.Cryptography", "SHA256", FrameworkReferences.Cryptography(module));
        var bufferType = new TypeRefUser(module, "System", "Buffer", module.CorLibTypes.AssemblyRef);

        var getExecutingAssembly = new MemberRefUser(module, "GetExecutingAssembly",
            MethodSig.CreateStatic(new ClassSig(assemblyType)), assemblyType);
        var getEntryAssembly = new MemberRefUser(module, "GetEntryAssembly",
            MethodSig.CreateStatic(new ClassSig(assemblyType)), assemblyType);
        var getLocation = new MemberRefUser(module, "get_Location",
            MethodSig.CreateInstance(module.CorLibTypes.String), assemblyType);
        var getProcessPath = new MemberRefUser(module, "get_ProcessPath",
            MethodSig.CreateStatic(module.CorLibTypes.String), environmentType);
        var isNullOrEmpty = new MemberRefUser(module, "IsNullOrEmpty",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String),
            new TypeRefUser(module, "System", "String", module.CorLibTypes.AssemblyRef));
        var failFast = new MemberRefUser(module, "FailFast",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.String), environmentType);
        var readAllBytes = new MemberRefUser(module, "ReadAllBytes",
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String), fileType);
        var sha256Create = new MemberRefUser(module, "Create",
            MethodSig.CreateStatic(new ClassSig(sha256Type)), sha256Type);
        var computeHash = new MemberRefUser(module, "ComputeHash",
            MethodSig.CreateInstance(new SZArraySig(module.CorLibTypes.Byte), new SZArraySig(module.CorLibTypes.Byte)),
            sha256Type);
        var arrayType = new TypeRefUser(module, "System", "Array", module.CorLibTypes.AssemblyRef);
        var blockCopy = new MemberRefUser(module, "BlockCopy",
            MethodSig.CreateStatic(
                module.CorLibTypes.Void,
                new ClassSig(arrayType),
                module.CorLibTypes.Int32,
                new ClassSig(arrayType),
                module.CorLibTypes.Int32,
                module.CorLibTypes.Int32),
            bufferType);
        var bitConverterType = module.CorLibTypes.GetTypeRef("System", "BitConverter");
        var toInt32 = new MemberRefUser(module, "ToInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.Int32),
            bitConverterType);

        var skipLabel = Instruction.Create(OpCodes.Ret);
        var exitLabel = Instruction.Create(OpCodes.Ldstr, "Obfy anti-tamper: assembly integrity check failed");

        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, verifiedField));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, skipLabel));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, verifiedField));

        var tryStart = Instruction.Create(OpCodes.Call, getExecutingAssembly);
        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getLocation));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, pathLocal));
        var havePath = Instruction.Create(OpCodes.Ldloc, pathLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pathLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, isNullOrEmpty));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, havePath));

        // Location empty: only fall back to ProcessPath when this assembly is the process
        // entry (single-file). ALC / LoadFromStream loads are a different assembly than
        // the host and must skip — hashing the launcher would false-fail.
        var skipPop = Instruction.Create(OpCodes.Pop);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getEntryAssembly));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, skipPop));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getExecutingAssembly));
        var processPath = Instruction.Create(OpCodes.Call, getProcessPath);
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, processPath));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));

        body.Instructions.Add(processPath);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, pathLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pathLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, isNullOrEmpty));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, havePath));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));
        body.Instructions.Add(skipPop);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));

        body.Instructions.Add(havePath);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readAllBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, bytesLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, findOffset));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        // Missing hash blob is tamper (or a failed patch), not the documented single-file skip.
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, exitLabel));

        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, storedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, storedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        var zeroCheck = Instruction.Create(OpCodes.Ldloc, iLocal);
        var zeroBody = Instruction.Create(OpCodes.Ldloc, bytesLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, zeroCheck));
        body.Instructions.Add(zeroBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(zeroCheck);
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, zeroBody));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, snOffLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize + 4));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, snSizeLocal));

        var afterSn = Instruction.Create(OpCodes.Call, sha256Create);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snOffLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, afterSn));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snSizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ble, afterSn));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snOffLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snSizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Bgt, afterSn));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        var snCheck = Instruction.Create(OpCodes.Ldloc, iLocal);
        var snBody = Instruction.Create(OpCodes.Ldloc, bytesLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, snCheck));
        body.Instructions.Add(snBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snOffLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(snCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, snSizeLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, snBody));

        body.Instructions.Add(afterSn);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, computeHash));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, computedLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        var cmpCheck = Instruction.Create(OpCodes.Ldloc, iLocal);
        var cmpBody = Instruction.Create(OpCodes.Ldloc, computedLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Br, cmpCheck));
        body.Instructions.Add(cmpBody);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, storedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, exitLabel));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(cmpCheck);
        body.Instructions.Add(Instruction.CreateLdcI4(AssemblyHashComputer.HashSize));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, cmpBody));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));

        body.Instructions.Add(exitLabel);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, failFast));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));

        var catchStart = Instruction.Create(OpCodes.Pop);
        body.Instructions.Add(catchStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Obfy anti-tamper: assembly integrity check failed"));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, failFast));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, skipLabel));
        body.Instructions.Add(skipLabel);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = skipLabel,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });

        body.UpdateInstructionOffsets();
        return method;
    }

    private bool InjectVerificationCall(MethodDef method, TypeDef antiTamperType)
    {
        if (!method.HasBody)
            return false;

        var verifyMethod = antiTamperType.FindMethod("Verify");
        if (verifyMethod == null)
            return false;

        var body = method.Body;
        var instructions = body.Instructions;

        // Insert call to Verify at the beginning
        instructions.Insert(0, Instruction.Create(OpCodes.Call, verifyMethod));

        body.UpdateInstructionOffsets();
        return true;
    }
}
