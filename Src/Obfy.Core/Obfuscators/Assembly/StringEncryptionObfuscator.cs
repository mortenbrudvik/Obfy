using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Encrypts string literals in .NET assemblies.
/// </summary>
public class StringEncryptionObfuscator : IObfuscator
{
    private readonly ILogger<StringEncryptionObfuscator> _logger;

    public StringEncryptionObfuscator(ILogger<StringEncryptionObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "StringEncryption";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.StringEncryption;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.StringEncryption.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.StringEncryption;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting string encryption with algorithm {Algorithm}", settings.Algorithm);

        try
        {
            // Generate encryption key for this assembly
            var key = EncryptionHelper.GenerateKey(settings.Algorithm);

            // Store key and encrypted strings for decryptor injection
            var encryptedStrings = new List<(int Index, string Encrypted)>();

            // Inject decryptor type
            var decryptorType = InjectDecryptorType(module, key, settings.Algorithm);
            var decryptMethod = decryptorType.FindMethod("Decrypt");

            // Process all methods
            foreach (var type in module.GetTypes())
            {
                if (ObfuscatorHelpers.IsRuntimeOrExcluded(type, context.Settings.Exclusions))
                    continue;

                if (ObfuscatorHelpers.IsCompilerGenerated(type))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    // Skip methods with exception handlers to avoid corrupting handler boundaries
                    if (method.Body.HasExceptionHandlers)
                    {
                        context.SkippedItems.Add(
                            SkippedItem.UnsupportedMethod(method.FullName, "Exception handlers"));
                        continue;
                    }

                    if (ObfuscatorHelpers.IsCompilerGeneratedMethod(method))
                        continue;


                    var body = method.Body;
                    var instructions = body.Instructions;
                    var modified = false;

                    for (var i = 0; i < instructions.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (instructions[i].OpCode != OpCodes.Ldstr)
                            continue;

                        var originalString = instructions[i].Operand as string;
                        if (string.IsNullOrEmpty(originalString))
                            continue;

                        if (originalString.Length < settings.MinStringLength)
                            continue;

                        // Encrypt the string
                        var encrypted = EncryptionHelper.EncryptToBase64(originalString, key, settings.Algorithm);
                        var index = encryptedStrings.Count;
                        encryptedStrings.Add((index, encrypted));

                        // Modify instruction IN PLACE to preserve branch targets
                        // Change: ldstr "original" -> ldc.i4 index; call Decrypt
                        var originalInstr = instructions[i];
                        ObfuscatorHelpers.SetLdcI4(originalInstr, index);
                        instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptMethod));
                        i++; // Skip the inserted instruction
                        modified = true;

                        stats.StringsEncrypted++;
                    }

                    // Fix branch targets and instruction offsets after modifications
                    if (modified)
                    {
                        // Simplify and optimize branches
                        body.SimplifyBranches();
                        body.OptimizeBranches();

                        // Ensure locals are properly initialized
                        if (body.InitLocals == false && body.Variables.Count > 0)
                            body.InitLocals = true;

                        // Update instruction offsets
                        body.UpdateInstructionOffsets();
                    }
                }
            }

            // Store encrypted strings in the decryptor type
            StoreEncryptedStrings(decryptorType, encryptedStrings);

            _logger.LogInformation("Encrypted {Count} strings", stats.StringsEncrypted);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "String encryption failed");
            return Task.FromResult(ObfuscationResult.Failed($"String encryption failed: {ex.Message}", ex));
        }
    }

    private TypeDef InjectDecryptorType(ModuleDef module, byte[] key, EncryptionAlgorithm algorithm)
    {
        // Create internal static class for decryption
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<StringDecryptor>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        // Add key field
        var keyField = new FieldDefUser(
            "_k",
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(keyField);

        // Add encrypted strings array field
        var stringsField = new FieldDefUser(
            "_s",
            new FieldSig(new SZArraySig(module.CorLibTypes.String)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(stringsField);

        // Add cache field for decrypted strings
        var cacheField = new FieldDefUser(
            "_c",
            new FieldSig(new SZArraySig(module.CorLibTypes.String)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(cacheField);

        var bytesDecrypt = algorithm == EncryptionAlgorithm.Aes256
            ? DecryptorIl.CreateAesDecryptBytes(module, "AesDecrypt", MethodAttributes.Private | MethodAttributes.Static)
            : DecryptorIl.CreateXorDecryptBytes(module, "XorDecrypt", MethodAttributes.Private | MethodAttributes.Static);
        typeDef.Methods.Add(bytesDecrypt);

        var decryptMethod = DecryptorIl.CreateStringDecrypt(module, keyField, stringsField, cacheField, bytesDecrypt, algorithm);
        typeDef.Methods.Add(decryptMethod);

        // Add static constructor to initialize key
        var cctor = CreateStaticConstructor(module, keyField, key);
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateStaticConstructor(ModuleDef module, FieldDef keyField, byte[] key)
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
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return cctor;
    }

    private void StoreEncryptedStrings(TypeDef decryptorType, List<(int Index, string Encrypted)> strings)
    {
        var stringsField = decryptorType.FindField("_s");
        var cctor = decryptorType.FindMethod(".cctor");

        if (cctor?.Body == null || stringsField == null)
            throw new InvalidOperationException("String decryptor storage is missing; refusing to emit a broken assembly.");

        var module = decryptorType.Module;
        var body = cctor.Body;

        // Remove the final ret instruction
        if (body.Instructions.Count > 0 && body.Instructions[^1].OpCode == OpCodes.Ret)
        {
            body.Instructions.RemoveAt(body.Instructions.Count - 1);
        }

        // Initialize strings array
        body.Instructions.Add(Instruction.CreateLdcI4(strings.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));

        foreach (var (index, encrypted) in strings)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(index));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, encrypted));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, stringsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
    }

}
