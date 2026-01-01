using System.Text.RegularExpressions;
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
    public int Priority => 15; // After string encryption (10), before control flow (30)

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ResourceEncryption.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.ResourceEncryption;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting resource encryption with algorithm {Algorithm}", settings.Algorithm);

        try
        {
            // Generate encryption key
            var key = EncryptionHelper.GenerateKey(settings.Algorithm);

            // Collect resources to encrypt
            var resourcesToEncrypt = new List<(string Name, byte[] Data)>();

            // Iterate backwards to safely remove while iterating
            for (var i = module.Resources.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (module.Resources[i] is not EmbeddedResource embeddedResource)
                    continue;

                var resourceName = embeddedResource.Name.String;

                if (!ShouldEncrypt(resourceName, settings))
                {
                    _logger.LogDebug("Skipping resource {Name} (excluded by pattern)", resourceName);
                    continue;
                }

                // Get original data
                var data = embeddedResource.CreateReader().ToArray();

                // Encrypt data
                var encrypted = EncryptionHelper.EncryptBytes(data, key, settings.Algorithm);

                resourcesToEncrypt.Add((resourceName, encrypted));

                // Remove original resource
                module.Resources.RemoveAt(i);

                stats.ResourcesEncrypted++;
                _logger.LogDebug("Encrypted resource {Name} ({Size} bytes)", resourceName, data.Length);
            }

            if (resourcesToEncrypt.Count > 0)
            {
                // Inject decryptor type
                var decryptorType = InjectDecryptorType(module, key, settings.Algorithm, resourcesToEncrypt);

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

    private static bool MatchesPattern(string name, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase);
    }

    private TypeDef InjectDecryptorType(
        ModuleDef module,
        byte[] key,
        EncryptionAlgorithm algorithm,
        List<(string Name, byte[] Data)> encryptedResources)
    {
        // Create internal static class for decryption
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<ResourceDecryptor>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        // Add key field
        var keyField = new FieldDefUser(
            "_k",
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(keyField);

        // Add encrypted resources data field (byte[][])
        var byteArrayType = new SZArraySig(module.CorLibTypes.Byte);
        var dataField = new FieldDefUser(
            "_d",
            new FieldSig(new SZArraySig(byteArrayType)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(dataField);

        // Add resource name to index mapping field (stored as parallel arrays for simplicity)
        var namesField = new FieldDefUser(
            "_n",
            new FieldSig(new SZArraySig(module.CorLibTypes.String)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(namesField);

        // Add cache field for decrypted resources
        var cacheField = new FieldDefUser(
            "_c",
            new FieldSig(new SZArraySig(byteArrayType)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(cacheField);

        // Add Decrypt method
        var decryptMethod = CreateDecryptMethod(module, keyField, dataField, namesField, cacheField, algorithm);
        typeDef.Methods.Add(decryptMethod);

        // Add static constructor to initialize data
        var cctor = CreateStaticConstructor(module, keyField, dataField, namesField, cacheField, key, encryptedResources);
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateDecryptMethod(
        ModuleDef module,
        FieldDef keyField,
        FieldDef dataField,
        FieldDef namesField,
        FieldDef cacheField,
        EncryptionAlgorithm algorithm)
    {
        var method = new MethodDefUser(
            "GetResource",
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // Local variables
        var indexLocal = new Local(module.CorLibTypes.Int32);
        var iLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(indexLocal);
        body.Variables.Add(iLocal);

        // Labels
        var loopStart = Instruction.Create(OpCodes.Nop);
        var loopCheck = Instruction.Create(OpCodes.Nop);
        var notFound = Instruction.Create(OpCodes.Nop);
        var returnCached = Instruction.Create(OpCodes.Nop);
        var decrypt = Instruction.Create(OpCodes.Nop);

        // index = -1
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, indexLocal));

        // i = 0
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loopCheck));

        // loopStart:
        body.Instructions.Add(loopStart);

        // if (_n[i] == resourceName) { index = i; break; }
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, namesField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));

        // Call String.op_Equality
        var stringType = module.CorLibTypes.String.ToTypeDefOrRef();
        var stringEqualityMethod = new MemberRefUser(
            module,
            "op_Equality",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.String, module.CorLibTypes.String),
            stringType);
        body.Instructions.Add(Instruction.Create(OpCodes.Call, stringEqualityMethod));

        var incrementI = Instruction.Create(OpCodes.Ldloc, iLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, incrementI));

        // index = i
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, notFound)); // break

        // i++
        body.Instructions.Add(incrementI);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        // loopCheck: while (i < _n.Length)
        body.Instructions.Add(loopCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, namesField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loopStart));

        // notFound: if (index == -1) return null
        body.Instructions.Add(notFound);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        var returnNull = Instruction.Create(OpCodes.Ldnull);
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, returnCached));
        body.Instructions.Add(returnNull);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // returnCached: if (_c[index] != null) return _c[index]
        body.Instructions.Add(returnCached);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, decrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        // decrypt: decrypt and cache
        body.Instructions.Add(decrypt);

        // _c[index] = Decrypt(_d[index], _k)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));

        // Load encrypted data
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));

        // Load key
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));

        // Decrypt inline (XOR for simplicity - embedded in IL)
        // For XOR: result[i] = encrypted[i] ^ key[i % key.Length]
        // This is complex in IL, so we'll use a simpler approach:
        // Store the decrypted result directly

        // For simplicity, we'll implement XOR decryption inline
        // Call a helper that we inject or do inline XOR
        if (algorithm == EncryptionAlgorithm.Xor)
        {
            // Call inline XOR helper
            var xorHelper = CreateXorDecryptHelper(module);
            body.Instructions.Add(Instruction.Create(OpCodes.Call, xorHelper));
        }
        else
        {
            // For AES, we need to call the runtime decryption
            // This is more complex - we'll inject a simpler approach
            var aesHelper = CreateAesDecryptHelper(module);
            body.Instructions.Add(Instruction.Create(OpCodes.Call, aesHelper));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));

        // return _c[index]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        body.OptimizeBranches();

        return method;
    }

    private MethodDef CreateXorDecryptHelper(ModuleDef module)
    {
        // Find or create XOR decrypt method
        var existingType = module.Find("Obfy.Runtime.<ResourceDecryptor>", false);
        var existingMethod = existingType?.FindMethod("XorDecrypt");
        if (existingMethod != null)
            return existingMethod;

        var method = new MethodDefUser(
            "XorDecrypt",
            MethodSig.CreateStatic(
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte)),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // byte[] result = new byte[data.Length]
        var resultLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var iLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(resultLocal);
        body.Variables.Add(iLocal);

        // result = new byte[data.Length]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));

        // i = 0
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        var loopStart = Instruction.Create(OpCodes.Ldloc, resultLocal);
        var loopCheck = Instruction.Create(OpCodes.Ldloc, iLocal);

        body.Instructions.Add(Instruction.Create(OpCodes.Br, loopCheck));

        // Loop body: result[i] = data[i] ^ key[i % key.Length]
        body.Instructions.Add(loopStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));

        // data[i]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

        // key[i % key.Length]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Rem));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

        // XOR
        body.Instructions.Add(Instruction.Create(OpCodes.Xor));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));

        // i++
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        // Check: while (i < data.Length)
        body.Instructions.Add(loopCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loopStart));

        // return result
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        body.OptimizeBranches();

        // Add to type
        existingType?.Methods.Add(method);

        return method;
    }

    private MethodDef CreateAesDecryptHelper(ModuleDef module)
    {
        // Create AES decrypt helper using System.Security.Cryptography.Aes
        var existingType = module.Find("Obfy.Runtime.<ResourceDecryptor>", false);
        var existingMethod = existingType?.FindMethod("AesDecrypt");
        if (existingMethod != null)
            return existingMethod;

        var method = new MethodDefUser(
            "AesDecrypt",
            MethodSig.CreateStatic(
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte)),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // For AES decryption, we need to use System.Security.Cryptography
        // This is complex to emit in IL, so we'll use a simplified approach
        // that extracts IV and decrypts

        // Find Aes.Create() method
        var aesTypeRef = new TypeRefUser(module, "System.Security.Cryptography", "Aes", module.CorLibTypes.AssemblyRef);
        var aesCreateSig = MethodSig.CreateStatic(new ClassSig(aesTypeRef));
        var aesCreate = new MemberRefUser(module, "Create", aesCreateSig, aesTypeRef);

        // Local variables
        var aesLocal = new Local(new ClassSig(aesTypeRef));
        var ivLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var encryptedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var decryptorLocal = new Local(module.CorLibTypes.Object); // ICryptoTransform
        var resultLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));

        body.Variables.Add(aesLocal);
        body.Variables.Add(ivLocal);
        body.Variables.Add(encryptedLocal);
        body.Variables.Add(decryptorLocal);
        body.Variables.Add(resultLocal);

        // var aes = Aes.Create()
        body.Instructions.Add(Instruction.Create(OpCodes.Call, aesCreate));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, aesLocal));

        // aes.Key = key
        var symmetricAlgTypeRef = new TypeRefUser(module, "System.Security.Cryptography", "SymmetricAlgorithm", module.CorLibTypes.AssemblyRef);
        var setKeySig = MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Byte));
        var setKey = new MemberRefUser(module, "set_Key", setKeySig, symmetricAlgTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, setKey));

        // iv = new byte[16] (AES block size)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ivLocal));

        // Buffer.BlockCopy(data, 0, iv, 0, 16)
        var bufferTypeRef = new TypeRefUser(module, "System", "Buffer", module.CorLibTypes.AssemblyRef);
        var blockCopySig = MethodSig.CreateStatic(
            module.CorLibTypes.Void,
            module.CorLibTypes.Object, module.CorLibTypes.Int32,
            module.CorLibTypes.Object, module.CorLibTypes.Int32,
            module.CorLibTypes.Int32);
        var blockCopy = new MemberRefUser(module, "BlockCopy", blockCopySig, bufferTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ivLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));

        // aes.IV = iv
        var setIVSig = MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Byte));
        var setIV = new MemberRefUser(module, "set_IV", setIVSig, symmetricAlgTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ivLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, setIV));

        // encrypted = new byte[data.Length - 16]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, encryptedLocal));

        // Buffer.BlockCopy(data, 16, encrypted, 0, encrypted.Length)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, encryptedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, encryptedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));

        // var decryptor = aes.CreateDecryptor()
        var createDecryptorSig = MethodSig.CreateInstance(module.CorLibTypes.Object); // Returns ICryptoTransform
        var createDecryptor = new MemberRefUser(module, "CreateDecryptor", createDecryptorSig, symmetricAlgTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, createDecryptor));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, decryptorLocal));

        // result = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length)
        var cryptoTransformTypeRef = new TypeRefUser(module, "System.Security.Cryptography", "ICryptoTransform", module.CorLibTypes.AssemblyRef);
        var transformFinalBlockSig = MethodSig.CreateInstance(
            new SZArraySig(module.CorLibTypes.Byte),
            new SZArraySig(module.CorLibTypes.Byte),
            module.CorLibTypes.Int32,
            module.CorLibTypes.Int32);
        var transformFinalBlock = new MemberRefUser(module, "TransformFinalBlock", transformFinalBlockSig, cryptoTransformTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptorLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, encryptedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, encryptedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, transformFinalBlock));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));

        // aes.Dispose()
        var disposeSig = MethodSig.CreateInstance(module.CorLibTypes.Void);
        var iDisposableTypeRef = new TypeRefUser(module, "System", "IDisposable", module.CorLibTypes.AssemblyRef);
        var dispose = new MemberRefUser(module, "Dispose", disposeSig, iDisposableTypeRef);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, dispose));

        // return result
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        existingType?.Methods.Add(method);

        return method;
    }

    private MethodDef CreateStaticConstructor(
        ModuleDef module,
        FieldDef keyField,
        FieldDef dataField,
        FieldDef namesField,
        FieldDef cacheField,
        byte[] key,
        List<(string Name, byte[] Data)> resources)
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

        // Initialize names array
        body.Instructions.Add(Instruction.CreateLdcI4(resources.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));

        for (var i = 0; i < resources.Count; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, resources[i].Name));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, namesField));

        // Initialize data array (byte[][])
        var byteArrayTypeRef = new SZArraySig(module.CorLibTypes.Byte).ToTypeDefOrRef();
        body.Instructions.Add(Instruction.CreateLdcI4(resources.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, byteArrayTypeRef));

        for (var i = 0; i < resources.Count; i++)
        {
            var data = resources[i].Data;

            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));

            // Create byte array for this resource
            body.Instructions.Add(Instruction.CreateLdcI4(data.Length));
            body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));

            // Initialize byte array with data
            // For large arrays, use RuntimeHelpers.InitializeArray with field data
            // For now, we'll embed small arrays directly and use a blob for larger ones
            if (data.Length <= 64)
            {
                // Inline small arrays
                for (var j = 0; j < data.Length; j++)
                {
                    body.Instructions.Add(Instruction.Create(OpCodes.Dup));
                    body.Instructions.Add(Instruction.CreateLdcI4(j));
                    body.Instructions.Add(Instruction.CreateLdcI4(data[j]));
                    body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
                }
            }
            else
            {
                // For larger arrays, use RuntimeHelpers.InitializeArray
                var dataFieldDef = new FieldDefUser(
                    $"_r{i}",
                    new FieldSig(new ValueTypeSig(CreatePrivateImplementationDetailsType(module, data.Length))),
                    FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA);

                dataFieldDef.InitialValue = data;

                var pidType = GetOrCreatePrivateImplementationDetails(module);
                pidType.Fields.Add(dataFieldDef);

                // Call RuntimeHelpers.InitializeArray
                var runtimeHelpersRef = new TypeRefUser(module, "System.Runtime.CompilerServices", "RuntimeHelpers", module.CorLibTypes.AssemblyRef);
                var initArraySig = MethodSig.CreateStatic(
                    module.CorLibTypes.Void,
                    new ClassSig(new TypeRefUser(module, "System", "Array", module.CorLibTypes.AssemblyRef)),
                    new ValueTypeSig(new TypeRefUser(module, "System", "RuntimeFieldHandle", module.CorLibTypes.AssemblyRef)));
                var initArray = new MemberRefUser(module, "InitializeArray", initArraySig, runtimeHelpersRef);

                body.Instructions.Add(Instruction.Create(OpCodes.Dup));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, dataFieldDef));
                body.Instructions.Add(Instruction.Create(OpCodes.Call, initArray));
            }

            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, dataField));

        // Initialize cache array (null elements by default)
        body.Instructions.Add(Instruction.CreateLdcI4(resources.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, byteArrayTypeRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, cacheField));

        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return cctor;
    }

    private TypeDef GetOrCreatePrivateImplementationDetails(ModuleDef module)
    {
        var existing = module.Find("<PrivateImplementationDetails>", false);
        if (existing != null)
            return existing;

        var pid = new TypeDefUser(
            string.Empty,
            "<PrivateImplementationDetails>",
            module.CorLibTypes.Object.TypeDefOrRef);

        pid.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed;
        module.Types.Add(pid);

        return pid;
    }

    private TypeDef CreatePrivateImplementationDetailsType(ModuleDef module, int size)
    {
        var pid = GetOrCreatePrivateImplementationDetails(module);
        var typeName = $"__StaticArrayInitTypeSize={size}";

        var existing = pid.NestedTypes.FirstOrDefault(t => t.Name == typeName);
        if (existing != null)
            return existing;

        var sizeType = new TypeDefUser(
            string.Empty,
            typeName,
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef));

        sizeType.Attributes = TypeAttributes.NestedPrivate | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout;
        sizeType.ClassLayout = new ClassLayoutUser(1, (uint)size);

        pid.NestedTypes.Add(sizeType);

        return sizeType;
    }
}
