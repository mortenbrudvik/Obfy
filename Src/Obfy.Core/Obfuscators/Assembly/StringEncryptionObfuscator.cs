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
            var indexXor = Random.Shared.Next(0x100, int.MaxValue) | 1;

            // Store key and encrypted strings for decryptor injection
            var encryptedStrings = new List<byte[]>();

            // Inject decryptor type with several entry points so a single Decrypt(int) is not a
            // decompiler signature for every string.
            var decryptorType = InjectDecryptorType(module, key, settings.Algorithm, indexXor);
            RuntimeInjection.Register(context, decryptorType);
            var decryptMethods = decryptorType.Methods
                .Where(m => m.MethodSig?.RetType.ElementType == ElementType.String
                    && m.MethodSig.Params.Count == 1
                    && m.MethodSig.Params[0].ElementType == ElementType.I4)
                .ToList();
            if (decryptMethods.Count == 0)
                throw new InvalidOperationException("String decryptor was not injected.");

            foreach (var type in module.GetTypes())
            {
                if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.Strings, context.Warnings))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.Strings, context.Warnings))
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
                        ObfuscatorHelpers.SetLdcI4(originalInstr, index ^ indexXor);
                        var decryptMethod = decryptMethods[index % decryptMethods.Count];
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
                EncryptResourceStrings(module, decryptorType, key, settings, stats, context);

            StoreEncryptedStrings(decryptorType, encryptedStrings);

            _logger.LogInformation("Encrypted {Count} strings", stats.StringsEncrypted);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "String encryption failed");
            return Task.FromResult(ObfuscationResult.Failed($"String encryption failed: {ex.Message}", ex));
        }
    }

    private TypeDef InjectDecryptorType(ModuleDef module, byte[] key, EncryptionAlgorithm algorithm, int indexXor)
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

        var indexXorField = new FieldDefUser(
            "_x",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(indexXorField);

        var bytesDecrypt = algorithm == EncryptionAlgorithm.Aes256
            ? DecryptorIl.CreateAesDecryptBytes(module, "AesDecrypt", MethodAttributes.Private | MethodAttributes.Static)
            : DecryptorIl.CreateXorDecryptBytes(module, "XorDecrypt", MethodAttributes.Private | MethodAttributes.Static);
        typeDef.Methods.Add(bytesDecrypt);

        const int variantCount = 3;
        for (var v = 0; v < variantCount; v++)
        {
            var name = v == 0 ? "Decrypt" : "Decrypt" + (v + 1);
            typeDef.Methods.Add(DecryptorIl.CreateStringDecrypt(
                module, keyField, stringsField, cacheField, indexXorField, bytesDecrypt, name));
        }

        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var body = new CilBody { InitLocals = true };
        cctor.Body = body;
        DecryptorIl.EmitEncodedKey(body, module, keyField, key);
        body.Instructions.Add(Instruction.CreateLdcI4(indexXor));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, indexXorField));
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

    internal const char ResourceCipherPrefix = '\u0001';

    private void EncryptResourceStrings(
        ModuleDef module,
        TypeDef decryptorType,
        byte[] key,
        StringEncryptionSettings settings,
        ObfuscationStatistics stats,
        PipelineContext context)
    {
        var encryptedAny = false;
        for (var i = 0; i < module.Resources.Count; i++)
        {
            if (module.Resources[i] is not EmbeddedResource embedded)
                continue;

            var name = embedded.Name.String;
            if (!name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = embedded.CreateReader().ToArray();
            if (!TryEncryptResourceFile(data, key, settings, out var rewritten, out var count, out var error))
            {
                if (error != null)
                    _logger.LogWarning(error, "Failed to rewrite resource strings in {Name}", name);
                context.SkippedItems.Add(SkippedItem.ResourceExcluded(name));
                continue;
            }

            module.Resources[i] = new EmbeddedResource(embedded.Name, rewritten, embedded.Attributes);
            stats.StringsEncrypted += count;
            encryptedAny = true;
            _logger.LogDebug("Encrypted {Count} resource strings in {Name}", count, name);
        }

        if (encryptedAny)
            RewriteResourceManagerGetString(module, decryptorType);
    }

    private static bool TryEncryptResourceFile(
        byte[] data,
        byte[] key,
        StringEncryptionSettings settings,
        out byte[] rewritten,
        out int count,
        out Exception? error)
    {
        rewritten = data;
        count = 0;
        error = null;
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
                    writer.AddResource(
                        enumerator.Key.ToString()!,
                        ResourceCipherPrefix + EncryptionHelper.EncryptToBase64(s, key, settings.Algorithm));
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
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or BadImageFormatException or System.Resources.MissingManifestResourceException)
        {
            error = ex;
            return false;
        }
    }

    private static void RewriteResourceManagerGetString(ModuleDef module, TypeDef decryptorType)
    {
        var bytesDecrypt = decryptorType.FindMethod("AesDecrypt") ?? decryptorType.FindMethod("XorDecrypt");
        var keyField = decryptorType.FindField("_k");
        if (bytesDecrypt == null || keyField == null)
            return;

        var decode = CreateResourceStringDecode(module, bytesDecrypt, keyField);
        decryptorType.Methods.Add(decode);
        var hook = CreateResourceStringHook(module, decode, paramCount: 1);
        var hook2 = CreateResourceStringHook(module, decode, paramCount: 2);
        decryptorType.Methods.Add(hook);
        decryptorType.Methods.Add(hook2);

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
                    var paramCount = called.MethodSig?.Params.Count ?? 0;
                    if (paramCount is not (1 or 2))
                        continue;

                    instr.OpCode = OpCodes.Call;
                    instr.Operand = paramCount == 1 ? hook : hook2;
                    modified = true;
                }

                if (modified)
                    method.Body.UpdateInstructionOffsets();
            }
        }
    }

    private static MethodDef CreateResourceStringHook(ModuleDef module, MethodDef decode, int paramCount)
    {
        var rmType = new TypeRefUser(module, "System.Resources", "ResourceManager", module.CorLibTypes.AssemblyRef);
        var cultureType = new TypeRefUser(module, "System.Globalization", "CultureInfo", module.CorLibTypes.AssemblyRef);

        MethodSig sig = paramCount == 1
            ? MethodSig.CreateStatic(module.CorLibTypes.String, new ClassSig(rmType), module.CorLibTypes.String)
            : MethodSig.CreateStatic(module.CorLibTypes.String, new ClassSig(rmType), module.CorLibTypes.String, new ClassSig(cultureType));

        var method = new MethodDefUser(
            paramCount == 1 ? "Ds" : "Ds2",
            sig,
            MethodAttributes.Assembly | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        method.Body = body;

        MemberRef getString = paramCount == 1
            ? new MemberRefUser(module, "GetString",
                MethodSig.CreateInstance(module.CorLibTypes.String, module.CorLibTypes.String), rmType)
            : new MemberRefUser(module, "GetString",
                MethodSig.CreateInstance(module.CorLibTypes.String, module.CorLibTypes.String, new ClassSig(cultureType)), rmType);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        if (paramCount == 2)
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getString));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, decode));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        return method;
    }

    private static MethodDef CreateResourceStringDecode(ModuleDef module, MethodDef bytesDecrypt, FieldDef keyField)
    {
        var encodingType = new TypeRefUser(module, "System.Text", "Encoding", module.CorLibTypes.AssemblyRef);
        var convertType = new TypeRefUser(module, "System", "Convert", module.CorLibTypes.AssemblyRef);
        var stringType = new TypeRefUser(module, "System", "String", module.CorLibTypes.AssemblyRef);

        var method = new MethodDefUser(
            "Dr",
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String),
            MethodAttributes.Private | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var sLocal = new Local(module.CorLibTypes.String);
        var resultLocal = new Local(module.CorLibTypes.String);
        body.Variables.Add(sLocal);
        body.Variables.Add(resultLocal);

        var fromBase64 = new MemberRefUser(module, "FromBase64String",
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String), convertType);
        var getUtf8 = new MemberRefUser(module, "get_UTF8",
            MethodSig.CreateStatic(new ClassSig(encodingType)), encodingType);
        var getStringBytes = new MemberRefUser(module, "GetString",
            MethodSig.CreateInstance(module.CorLibTypes.String, new SZArraySig(module.CorLibTypes.Byte)), encodingType);
        var getLength = new MemberRefUser(module, "get_Length",
            MethodSig.CreateInstance(module.CorLibTypes.Int32), stringType);
        var getChars = new MemberRefUser(module, "get_Chars",
            MethodSig.CreateInstance(module.CorLibTypes.Char, module.CorLibTypes.Int32), stringType);
        var substring = new MemberRefUser(module, "Substring",
            MethodSig.CreateInstance(module.CorLibTypes.String, module.CorLibTypes.Int32), stringType);

        var retPlain = Instruction.Create(OpCodes.Ldloc, sLocal);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var tryStart = Instruction.Create(OpCodes.Call, getUtf8);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, retPlain));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getLength));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ble, retPlain));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getChars));
        body.Instructions.Add(Instruction.CreateLdcI4(ResourceCipherPrefix));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, retPlain));

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, substring));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, fromBase64));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, bytesDecrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getStringBytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));
        var afterTry = Instruction.Create(OpCodes.Ldloc, resultLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, afterTry));

        body.Instructions.Add(catchPop);
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, retPlain));
        body.Instructions.Add(retPlain);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.Instructions.Add(afterTry);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = retPlain,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });

        body.UpdateInstructionOffsets();
        return method;
    }
}
