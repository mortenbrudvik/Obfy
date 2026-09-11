using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Encrypts embedded resources in .NET assemblies.
/// </summary>
public class ResourceEncryptionObfuscator : IObfuscator
{
    private readonly ILogger<ResourceEncryptionObfuscator> _logger;

    public ResourceEncryptionObfuscator(ILogger<ResourceEncryptionObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "ResourceEncryption";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.ResourceEncryption;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ResourceEncryption.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.ResourceEncryption;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting resource encryption with algorithm {Algorithm}", settings.Algorithm);

        try
        {
            // Generate encryption key
            var key = EncryptionHelper.GenerateKey(settings.Algorithm);

            // Names of the resources we actually encrypt; the injected loader decrypts only these
            // and passes any other resource stream through untouched.
            var encryptedResourceNames = new List<string>();

            for (var i = 0; i < module.Resources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (module.Resources[i] is not EmbeddedResource embeddedResource)
                {
                    context.Warnings.Add(
                        $"ResourceEncryption: '{module.Resources[i].Name}' is not an embedded resource and cannot be encrypted.");
                    continue;
                }

                var resourceName = embeddedResource.Name.String;

                if (!ShouldEncrypt(resourceName, settings))
                {
                    context.SkippedItems.Add(SkippedItem.ResourceExcluded(resourceName));
                    _logger.LogDebug("Skipping resource {Name} (excluded by pattern)", resourceName);
                    continue;
                }

                // Get original data
                var data = embeddedResource.CreateReader().ToArray();

                // Encrypt data and replace the resource in place so no plaintext remains in the assembly.
                var encrypted = EncryptionHelper.EncryptBytes(data, key, settings.Algorithm);

                module.Resources[i] = new EmbeddedResource(
                    embeddedResource.Name,
                    encrypted,
                    embeddedResource.Attributes);

                encryptedResourceNames.Add(resourceName);
                stats.ResourcesEncrypted++;
                _logger.LogDebug("Encrypted resource {Name} ({Size} bytes)", resourceName, data.Length);
            }

            if (encryptedResourceNames.Count > 0)
            {
                // Inject decryptor type and rewrite resource loads to decrypt on the fly
                InjectDecryptorType(module, key, settings.Algorithm, encryptedResourceNames, context);

                _logger.LogInformation("Encrypted {Count} resources", stats.ResourcesEncrypted);
            }
            else
            {
                _logger.LogInformation("No resources found to encrypt");
            }

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resource encryption failed");
            return Task.FromResult(ObfuscationResult.Failed($"Resource encryption failed: {ex.Message}", ex));
        }
    }

    private bool ShouldEncrypt(string resourceName, ResourceEncryptionSettings settings)
    {
        // Check exclude patterns first (they take precedence)
        if (settings.ExcludePatterns.Any(p => MatchesPattern(resourceName, p)))
            return false;

        // Check include patterns
        return settings.IncludePatterns.Any(p => MatchesPattern(resourceName, p));
    }

    private static bool MatchesPattern(string name, string pattern) => WildcardMatcher.IsMatch(name, pattern);

    private void InjectDecryptorType(
        ModuleDef module,
        byte[] key,
        EncryptionAlgorithm algorithm,
        List<string> encryptedResourceNames,
        PipelineContext context)
    {
        // Create internal static class for decryption
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<ResourceDecryptor>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        // Key field
        var keyField = new FieldDefUser(
            "_k",
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(keyField);

        // Names of encrypted resources (the loader only decrypts a stream whose name is listed here)
        var namesField = new FieldDefUser(
            "_n",
            new FieldSig(new SZArraySig(module.CorLibTypes.String)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(namesField);

        // Algorithm-aware byte[] -> byte[] decryptor that inverts EncryptionHelper.EncryptBytes.
        var bytesDecrypt = algorithm == EncryptionAlgorithm.Aes256
            ? DecryptorIl.CreateAesDecryptBytes(module, "D", MethodAttributes.Private | MethodAttributes.Static)
            : DecryptorIl.CreateXorDecryptBytes(module, "D", MethodAttributes.Private | MethodAttributes.Static);
        typeDef.Methods.Add(bytesDecrypt);

        var loadResource = CreateLoadResourceStreamMethod(module, bytesDecrypt, keyField, namesField);
        typeDef.Methods.Add(loadResource);

        // Static constructor initializes the key and encrypted-resource-name table.
        var cctor = CreateStaticConstructor(module, keyField, namesField, key, encryptedResourceNames);
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        if (RewriteResourceLoads(module, loadResource, context) == 0)
        {
            context.Warnings.Add(
                "ResourceEncryption: resources were encrypted but no Assembly.GetManifestResourceStream(string) call sites were rewritten. Encrypted resources loaded any other way will be ciphertext.");
        }
    }

    /// <summary>
    /// Emits <c>static Stream LoadResourceStream(Assembly asm, string name)</c>. It fetches the
    /// manifest resource stream, and if <c>name</c> is one of the encrypted resources,
    /// decrypts the stream and returns it as a new MemoryStream; otherwise the original stream is
    /// returned unchanged so non-encrypted resources are never corrupted.
    /// </summary>
    private static MethodDef CreateLoadResourceStreamMethod(
        ModuleDef module,
        MethodDef bytesDecrypt,
        FieldDef keyField,
        FieldDef namesField)
    {
        var streamType = new TypeRefUser(module, "System.IO", "Stream", module.CorLibTypes.AssemblyRef);
        var memoryStreamType = new TypeRefUser(module, "System.IO", "MemoryStream", module.CorLibTypes.AssemblyRef);
        var assemblyType = new TypeRefUser(module, "System.Reflection", "Assembly", module.CorLibTypes.AssemblyRef);
        var stringType = module.CorLibTypes.String.ToTypeDefOrRef();

        var method = new MethodDefUser(
            "LoadResourceStream",
            MethodSig.CreateStatic(new ClassSig(streamType), new ClassSig(assemblyType), module.CorLibTypes.String),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var streamLocal = new Local(new ClassSig(streamType));
        var bufferLocal = new Local(new ClassSig(memoryStreamType));
        var iLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(streamLocal);
        body.Variables.Add(bufferLocal);
        body.Variables.Add(iLocal);

        var getStream = new MemberRefUser(module, "GetManifestResourceStream",
            MethodSig.CreateInstance(new ClassSig(streamType), module.CorLibTypes.String), assemblyType);
        var stringEquality = new MemberRefUser(module, "op_Equality",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String, module.CorLibTypes.String),
            stringType);
        var copyTo = new MemberRefUser(module, "CopyTo",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(streamType)), streamType);
        var dispose = new MemberRefUser(module, "Dispose",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            new TypeRefUser(module, "System", "IDisposable", module.CorLibTypes.AssemblyRef));
        var toArray = new MemberRefUser(module, "ToArray",
            MethodSig.CreateInstance(new SZArraySig(module.CorLibTypes.Byte)), memoryStreamType);
        var msByteCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Byte)), memoryStreamType);
        var msEmptyCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void), memoryStreamType);

        var loopCheck = Instruction.Create(OpCodes.Ldloc, iLocal);
        var loopBody = Instruction.Create(OpCodes.Ldsfld, namesField);
        var encrypted = Instruction.Create(OpCodes.Ldloc, streamLocal);
        var readAndDecrypt = Instruction.Create(OpCodes.Newobj, msEmptyCtor);

        // stream = asm.GetManifestResourceStream(name);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getStream));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, streamLocal));

        // for (i = 0; i < _n.Length; i++) if (_n[i] == name) goto encrypted;
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loopCheck));

        body.Instructions.Add(loopBody); // ldsfld _n
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, stringEquality));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, encrypted));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        body.Instructions.Add(loopCheck); // ldloc i
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, namesField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loopBody));

        // not encrypted -> return original stream unchanged
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // encrypted: if (stream == null) return null;
        body.Instructions.Add(encrypted); // ldloc stream
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, readAndDecrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // buffer = new MemoryStream(); stream.CopyTo(buffer); stream.Dispose();
        body.Instructions.Add(readAndDecrypt); // newobj MemoryStream()
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, bufferLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufferLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, copyTo));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, dispose));

        // return new MemoryStream(D(buffer.ToArray(), _k));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufferLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, toArray));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, bytesDecrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, msByteCtor));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    private static MethodDef CreateStaticConstructor(
        ModuleDef module,
        FieldDef keyField,
        FieldDef namesField,
        byte[] key,
        List<string> resourceNames)
    {
        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);

        var body = new CilBody();
        cctor.Body = body;

        // Initialize key array
        body.Instructions.Add(Instruction.CreateLdcI4(key.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));

        for (var i = 0; i < key.Length; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.CreateLdcI4(key[i]));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, keyField));

        // Initialize encrypted-resource-name array
        body.Instructions.Add(Instruction.CreateLdcI4(resourceNames.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));

        for (var i = 0; i < resourceNames.Count; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, resourceNames[i]));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, namesField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return cctor;
    }

    /// <summary>
    /// Replaces <c>Assembly.GetManifestResourceStream(string)</c> (matched by declaring type name
    /// and arity 1) with a call to the injected name-aware loader. The <c>(Type, string)</c>
    /// overload and <c>Module.GetManifestResourceStream</c> are not intercepted and will observe
    /// ciphertext; those call sites are recorded as warnings.
    /// </summary>
    private static int RewriteResourceLoads(ModuleDef module, MethodDef loadResource, PipelineContext context)
    {
        var rewritten = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.Namespace == "Obfy.Runtime")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                var instructions = method.Body.Instructions;
                var modified = false;
                foreach (var instr in instructions)
                {
                    if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                        continue;

                    if (instr.Operand is not IMethod called)
                        continue;

                    if (called.Name != "GetManifestResourceStream")
                        continue;

                    var declaring = called.DeclaringType?.Name?.String;
                    var arity = called.MethodSig?.Params.Count ?? -1;
                    var isAssembly = declaring is "Assembly" or "RuntimeAssembly";

                    if (isAssembly && arity == 1)
                    {
                        // Rewrite in place to preserve any branch targets pointing at this instruction.
                        instr.OpCode = OpCodes.Call;
                        instr.Operand = loadResource;
                        modified = true;
                        rewritten++;
                        continue;
                    }

                    context.Warnings.Add(
                        $"ResourceEncryption: {method.FullName} calls {called.FullName}, which is not rewritten. Encrypted resources loaded this way will be ciphertext.");
                }

                if (modified)
                    method.Body.UpdateInstructionOffsets();
            }
        }

        return rewritten;
    }
}
