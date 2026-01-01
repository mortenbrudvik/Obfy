using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects anti-tampering protection into assemblies.
/// Computes hash of method bodies at obfuscation time and verifies at runtime.
/// </summary>
public class AntiTamperObfuscator : IObfuscator
{
    private readonly ILogger<AntiTamperObfuscator> _logger;

    /// <summary>
    /// Shared data key for storing hash field metadata for post-processing.
    /// </summary>
    public const string HashFieldMetadataKey = "AntiTamper.HashFieldMetadata";

    public AntiTamperObfuscator(ILogger<AntiTamperObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "AntiTamper";

    /// <inheritdoc/>
    public int Priority => 75;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiTamper.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.Protection.AntiTamper;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting anti-tamper protection injection");

        try
        {
            // Inject anti-tamper type with verification logic
            var antiTamperType = InjectAntiTamperType(module);

            // Store metadata for post-processing (hash patching)
            var hashField = antiTamperType.Fields.FirstOrDefault(f => f.Name == "_h");
            if (hashField != null)
            {
                context.SharedData[HashFieldMetadataKey] = new AntiTamperMetadata
                {
                    HashFieldToken = hashField.MDToken.Raw
                };
            }

            // Add check to entry point if configured
            if (settings.CheckEntryPoint && module.EntryPoint != null)
            {
                InjectVerificationCall(module.EntryPoint, antiTamperType);
                stats.ProtectionsApplied++;
                _logger.LogDebug("Injected anti-tamper check at entry point");
            }

            // Add check to module initializer if configured
            if (settings.CheckModuleInitializer)
            {
                var moduleInitializer = FindOrCreateModuleInitializer(module);
                if (moduleInitializer != null)
                {
                    InjectVerificationCall(moduleInitializer, antiTamperType);
                    stats.ProtectionsApplied++;
                    _logger.LogDebug("Injected anti-tamper check at module initializer");
                }
            }

            _logger.LogInformation("Applied {Count} anti-tamper protections", stats.ProtectionsApplied);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
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

        // Add hash field (placeholder - will be patched during post-processing)
        var hashField = new FieldDefUser(
            "_h",
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(hashField);

        // Add text section offset field (for runtime hash computation)
        var offsetField = new FieldDefUser(
            "_o",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(offsetField);

        // Add text section size field
        var sizeField = new FieldDefUser(
            "_s",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(sizeField);

        // Add verified flag (to only check once)
        var verifiedField = new FieldDefUser(
            "_v",
            new FieldSig(module.CorLibTypes.Boolean),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(verifiedField);

        // Add static constructor to initialize hash field with placeholder
        var cctor = CreateStaticConstructor(module, hashField, offsetField, sizeField);
        typeDef.Methods.Add(cctor);

        // Add Verify method
        var verifyMethod = CreateVerifyMethod(module, hashField, offsetField, sizeField, verifiedField);
        typeDef.Methods.Add(verifyMethod);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateStaticConstructor(ModuleDef module, FieldDef hashField, FieldDef offsetField, FieldDef sizeField)
    {
        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName);

        var body = new CilBody { InitLocals = true };
        cctor.Body = body;

        // Initialize hash field with 32 zero bytes (placeholder)
        // These will be patched with the actual hash during post-processing
        body.Instructions.Add(Instruction.CreateLdcI4(32));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, hashField));

        // Initialize offset with placeholder (will be patched)
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, offsetField));

        // Initialize size with placeholder (will be patched)
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, sizeField));

        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return cctor;
    }

    private MethodDef CreateVerifyMethod(ModuleDef module, FieldDef hashField, FieldDef offsetField, FieldDef sizeField, FieldDef verifiedField)
    {
        var method = new MethodDefUser(
            "Verify",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // Local variables
        var pathLocal = new Local(module.CorLibTypes.String);
        var bytesLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var hashLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var iLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(pathLocal);
        body.Variables.Add(bytesLocal);
        body.Variables.Add(hashLocal);
        body.Variables.Add(iLocal);

        // Get type references
        var assemblyType = new TypeRefUser(module, "System.Reflection", "Assembly", module.CorLibTypes.AssemblyRef);
        var environmentType = module.CorLibTypes.GetTypeRef("System", "Environment");
        var fileType = new TypeRefUser(module, "System.IO", "File", module.CorLibTypes.AssemblyRef);
        var sha256Type = new TypeRefUser(module, "System.Security.Cryptography", "SHA256", module.CorLibTypes.AssemblyRef);
        var stringType = module.CorLibTypes.String;

        // Method references
        var getExecutingAssembly = new MemberRefUser(module, "GetExecutingAssembly",
            MethodSig.CreateStatic(new ClassSig(assemblyType)), assemblyType);

        var getLocation = new MemberRefUser(module, "get_Location",
            MethodSig.CreateInstance(module.CorLibTypes.String), assemblyType);

        var isNullOrEmpty = new MemberRefUser(module, "IsNullOrEmpty",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String),
            new TypeRefUser(module, "System", "String", module.CorLibTypes.AssemblyRef));

        var exitMethod = new MemberRefUser(module, "Exit",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.Int32), environmentType);

        var readAllBytes = new MemberRefUser(module, "ReadAllBytes",
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String), fileType);

        var sha256Create = new MemberRefUser(module, "Create",
            MethodSig.CreateStatic(new ClassSig(sha256Type)), sha256Type);

        var computeHash = new MemberRefUser(module, "ComputeHash",
            MethodSig.CreateInstance(new SZArraySig(module.CorLibTypes.Byte), new SZArraySig(module.CorLibTypes.Byte)),
            sha256Type);

        // Labels for branching
        var skipLabel = Instruction.Create(OpCodes.Ret);
        var loopStart = Instruction.Create(OpCodes.Ldloc, iLocal);
        var exitLabel = Instruction.Create(OpCodes.Ldc_I4_1);

        // Check if already verified
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, verifiedField));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue_S, skipLabel));

        // Mark as verified (do this early to prevent re-entry)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, verifiedField));

        // Get assembly path: Assembly.GetExecutingAssembly().Location
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getExecutingAssembly));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getLocation));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, pathLocal));

        // Check if path is null or empty - if so, skip verification (single-file app)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pathLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, isNullOrEmpty));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue_S, skipLabel));

        // Read file bytes: File.ReadAllBytes(path)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, pathLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, readAllBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, bytesLocal));

        // Compute hash: SHA256.Create().ComputeHash(bytes)
        body.Instructions.Add(Instruction.Create(OpCodes.Call, sha256Create));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, computeHash));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, hashLocal));

        // Compare hashes - loop through 32 bytes
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        // Loop start
        body.Instructions.Add(loopStart);
        body.Instructions.Add(Instruction.CreateLdcI4(32));
        body.Instructions.Add(Instruction.Create(OpCodes.Bge_S, skipLabel)); // If i >= 32, verification passed

        // Compare bytes: if (hash[i] != _h[i]) exit
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, hashLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, hashField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un_S, exitLabel)); // If not equal, exit

        // i++
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br_S, loopStart));

        // Exit label: Environment.Exit(1)
        body.Instructions.Add(exitLabel);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, exitMethod));

        // Skip label (return)
        body.Instructions.Add(skipLabel);

        body.UpdateInstructionOffsets();

        return method;
    }

    private void InjectVerificationCall(MethodDef method, TypeDef antiTamperType)
    {
        if (!method.HasBody)
            return;

        var verifyMethod = antiTamperType.FindMethod("Verify");
        if (verifyMethod == null)
            return;

        var body = method.Body;
        var instructions = body.Instructions;

        // Insert call to Verify at the beginning
        instructions.Insert(0, Instruction.Create(OpCodes.Call, verifyMethod));

        body.UpdateInstructionOffsets();
    }

    private MethodDef? FindOrCreateModuleInitializer(ModuleDef module)
    {
        var globalType = module.GlobalType;
        if (globalType == null)
        {
            // Create global type if it doesn't exist
            globalType = new TypeDefUser("", "<Module>", null);
            globalType.Attributes = TypeAttributes.NotPublic;
            module.Types.Insert(0, globalType);
        }

        // Find existing .cctor
        var cctor = globalType.Methods.FirstOrDefault(m =>
            m.IsStaticConstructor || m.Name == ".cctor");

        if (cctor != null)
            return cctor;

        // Create new .cctor
        cctor = new MethodDefUser(
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

/// <summary>
/// Metadata for anti-tamper post-processing.
/// </summary>
public class AntiTamperMetadata
{
    /// <summary>
    /// Token of the hash field for locating it during patching.
    /// </summary>
    public uint HashFieldToken { get; set; }
}
