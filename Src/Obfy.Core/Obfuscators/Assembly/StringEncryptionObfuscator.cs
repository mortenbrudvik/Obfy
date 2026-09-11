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
            var encryptedStrings = new List<byte[]>();

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

                    if (ObfuscatorHelpers.MethodMatchesExclusion(method, context.Settings.Exclusions))
                        continue;

                    var body = method.Body;
                    var instructions = body.Instructions;
                    var modified = false;

                    if (settings.EncryptConstantStrings)
                    {
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

                        var encrypted = EncryptionHelper.Encrypt(originalString, key, settings.Algorithm);
                        var index = encryptedStrings.Count;
                        encryptedStrings.Add(encrypted);

                        var originalInstr = instructions[i];
                        ObfuscatorHelpers.SetLdcI4(originalInstr, index);
                        instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptMethod));
                        i++;
                        modified = true;

                        stats.StringsEncrypted++;
                    }
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

            if (settings.EncryptResourceStrings)
                EncryptResourceStrings(module, decryptorType, key, settings, encryptedStrings, stats, context);

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

        var stringsField = new FieldDefUser(
            "_s",
            new FieldSig(new SZArraySig(new SZArraySig(module.CorLibTypes.Byte))),
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

        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var body = new CilBody { InitLocals = true };
        cctor.Body = body;
        DecryptorIl.EmitEncodedKey(body, module, keyField, key);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private static void StoreEncryptedStrings(TypeDef decryptorType, List<byte[]> strings)
    {
        var stringsField = decryptorType.FindField("_s");
        var cctor = decryptorType.FindMethod(".cctor");

        if (cctor?.Body == null || stringsField == null)
            throw new InvalidOperationException("String decryptor storage is missing; refusing to emit a broken assembly.");

        var body = cctor.Body;
        if (body.Instructions.Count > 0 && body.Instructions[^1].OpCode == OpCodes.Ret)
            body.Instructions.RemoveAt(body.Instructions.Count - 1);

        DecryptorIl.EmitByteArrayArray(body, decryptorType.Module, stringsField, strings);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
    }

    private void EncryptResourceStrings(
        ModuleDef module,
        TypeDef decryptorType,
        byte[] key,
        StringEncryptionSettings settings,
        List<byte[]> encryptedStrings,
        ObfuscationStatistics stats,
        PipelineContext context)
    {
        for (var i = 0; i < module.Resources.Count; i++)
        {
            if (module.Resources[i] is not EmbeddedResource embedded)
                continue;

            var name = embedded.Name.String;
            if (!name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = embedded.CreateReader().ToArray();
            if (!TryEncryptResourceFile(data, key, settings, out var rewritten, out var count))
            {
                context.SkippedItems.Add(SkippedItem.ResourceExcluded(name));
                continue;
            }

            module.Resources[i] = new EmbeddedResource(embedded.Name, rewritten, embedded.Attributes);
            stats.StringsEncrypted += count;
            _logger.LogDebug("Encrypted {Count} resource strings in {Name}", count, name);
        }

        RewriteResourceManagerGetString(module, decryptorType);
    }

    private static bool TryEncryptResourceFile(
        byte[] data,
        byte[] key,
        StringEncryptionSettings settings,
        out byte[] rewritten,
        out int count)
    {
        rewritten = data;
        count = 0;
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var reader = new System.Resources.ResourceReader(input);
            using var output = new MemoryStream();
            using var writer = new System.Resources.ResourceWriter(output);

            var enumerator = reader.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var value = enumerator.Value;
                if (value is string s && s.Length >= settings.MinStringLength)
                {
                    writer.AddResource(enumerator.Key.ToString()!, EncryptionHelper.EncryptToBase64(s, key, settings.Algorithm));
                    count++;
                }
                else
                {
                    writer.AddResource(enumerator.Key.ToString()!, value);
                }
            }

            writer.Generate();
            rewritten = output.ToArray();
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RewriteResourceManagerGetString(ModuleDef module, TypeDef decryptorType)
    {
        var bytesDecrypt = decryptorType.FindMethod("AesDecrypt") ?? decryptorType.FindMethod("XorDecrypt");
        var keyField = decryptorType.FindField("_k");
        if (bytesDecrypt == null || keyField == null)
            return;

        var hook = CreateResourceStringHook(module, bytesDecrypt, keyField);
        decryptorType.Methods.Add(hook);

        foreach (var type in module.GetTypes())
        {
            if (ObfuscatorHelpers.IsRuntimeHelper(type))
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                var modified = false;
                foreach (var instr in method.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                        continue;
                    if (instr.Operand is not IMethod called)
                        continue;
                    if (called.Name != "GetString")
                        continue;
                    if (called.DeclaringType?.Name != "ResourceManager")
                        continue;
                    if ((called.MethodSig?.Params.Count ?? 0) != 1)
                        continue;

                    instr.OpCode = OpCodes.Call;
                    instr.Operand = hook;
                    modified = true;
                }

                if (modified)
                    method.Body.UpdateInstructionOffsets();
            }
        }
    }

    private static MethodDef CreateResourceStringHook(ModuleDef module, MethodDef bytesDecrypt, FieldDef keyField)
    {
        var rmType = new TypeRefUser(module, "System.Resources", "ResourceManager", module.CorLibTypes.AssemblyRef);
        var encodingType = new TypeRefUser(module, "System.Text", "Encoding", module.CorLibTypes.AssemblyRef);
        var convertType = new TypeRefUser(module, "System", "Convert", module.CorLibTypes.AssemblyRef);

        var method = new MethodDefUser(
            "Ds",
            MethodSig.CreateStatic(module.CorLibTypes.String, new ClassSig(rmType), module.CorLibTypes.String),
            MethodAttributes.Assembly | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var sLocal = new Local(module.CorLibTypes.String);
        body.Variables.Add(sLocal);

        var getString = new MemberRefUser(module, "GetString",
            MethodSig.CreateInstance(module.CorLibTypes.String, module.CorLibTypes.String), rmType);
        var fromBase64 = new MemberRefUser(module, "FromBase64String",
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String), convertType);
        var getUtf8 = new MemberRefUser(module, "get_UTF8",
            MethodSig.CreateStatic(new ClassSig(encodingType)), encodingType);
        var getStringBytes = new MemberRefUser(module, "GetString",
            MethodSig.CreateInstance(module.CorLibTypes.String, new SZArraySig(module.CorLibTypes.Byte)), encodingType);

        var retNull = Instruction.Create(OpCodes.Ldloc, sLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getString));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, retNull));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getUtf8));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, fromBase64));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, bytesDecrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getStringBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.Instructions.Add(retNull);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        return method;
    }
}
