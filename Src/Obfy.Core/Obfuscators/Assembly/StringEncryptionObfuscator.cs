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
    public int Priority => 10;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.StringEncryption.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
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
                if (IsExcluded(type, context.Settings.Exclusions))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var body = method.Body;
                    var instructions = body.Instructions;

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

                        // Replace: ldstr "original" -> ldc.i4 index; call Decrypt
                        instructions[i] = Instruction.CreateLdcI4(index);
                        instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptMethod));
                        i++; // Skip the inserted instruction

                        stats.StringsEncrypted++;
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

        // Add Decrypt method
        var decryptMethod = CreateDecryptMethod(module, keyField, stringsField, cacheField, algorithm);
        typeDef.Methods.Add(decryptMethod);

        // Add static constructor to initialize key
        var cctor = CreateStaticConstructor(module, keyField, key);
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateDecryptMethod(
        ModuleDef module,
        FieldDef keyField,
        FieldDef stringsField,
        FieldDef cacheField,
        EncryptionAlgorithm algorithm)
    {
        var method = new MethodDefUser(
            "Decrypt",
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        method.Body = body;

        // Simple implementation that returns the encrypted string for now
        // Full implementation would include decryption logic
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, stringsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return method;
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
            return;

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

    private bool IsExcluded(TypeDef type, ExclusionRules rules)
    {
        if (type.Namespace == "Obfy.Runtime")
            return true;

        return rules.Namespaces.Any(n => MatchesPattern(type.Namespace, n)) ||
               rules.Types.Any(t => MatchesPattern(type.Name, t));
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
