using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Encrypts numeric constants (int, long, float, double) in .NET assemblies.
/// </summary>
public class ConstantEncryptionObfuscator : IObfuscator
{
    private readonly ILogger<ConstantEncryptionObfuscator> _logger;

    /// <summary>
    /// Represents the type of a numeric constant.
    /// </summary>
    private enum ConstantType { Int32, Int64, Single, Double }

    /// <summary>
    /// Represents an encrypted constant with its index, encrypted bytes, and type.
    /// </summary>
    private record EncryptedConstant(int Index, byte[] EncryptedBytes, ConstantType Type);

    public ConstantEncryptionObfuscator(ILogger<ConstantEncryptionObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "ConstantEncryption";

    /// <inheritdoc/>
    public int Priority => 11; // After StringEncryption (10), before ResourceEncryption (15)

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ConstantEncryption.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.ConstantEncryption;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting constant encryption with algorithm {Algorithm}", settings.Algorithm);

        try
        {
            // Generate encryption key for this assembly
            var key = EncryptionHelper.GenerateKey(settings.Algorithm);

            // Store encrypted constants
            var encryptedConstants = new List<EncryptedConstant>();

            // Inject decryptor type with methods for each constant type
            var decryptorType = InjectDecryptorType(module, key, settings.Algorithm);
            var decryptInt32 = decryptorType.FindMethod("DecryptInt32");
            var decryptInt64 = decryptorType.FindMethod("DecryptInt64");
            var decryptSingle = decryptorType.FindMethod("DecryptSingle");
            var decryptDouble = decryptorType.FindMethod("DecryptDouble");

            // Process all methods
            foreach (var type in module.GetTypes())
            {
                if (IsExcluded(type, context.Settings.Exclusions))
                    continue;

                // Skip compiler-generated types
                if (IsCompilerGenerated(type))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    // Skip methods with exception handlers to avoid corrupting handler boundaries
                    if (method.Body.HasExceptionHandlers)
                        continue;

                    // Skip compiler-generated methods
                    if (IsCompilerGeneratedMethod(method))
                        continue;

                    var body = method.Body;
                    var instructions = body.Instructions;
                    var modified = false;

                    for (var i = 0; i < instructions.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var instr = instructions[i];

                        // Try Int32 constants
                        if (settings.EncryptIntegers)
                        {
                            var intValue = ExtractInt32Value(instr);
                            if (intValue.HasValue && !ShouldSkipInt32(intValue.Value, settings))
                            {
                                var encrypted = EncryptInt32(intValue.Value, key, settings.Algorithm);
                                var index = encryptedConstants.Count;
                                encryptedConstants.Add(new EncryptedConstant(index, encrypted, ConstantType.Int32));

                                SetLdcI4(instr, index);
                                instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptInt32));
                                i++;
                                modified = true;
                                stats.ConstantsEncrypted++;
                                continue;
                            }
                        }

                        // Try Int64 constants
                        if (settings.EncryptLongs)
                        {
                            var longValue = ExtractInt64Value(instr);
                            if (longValue.HasValue && !ShouldSkipInt64(longValue.Value, settings))
                            {
                                var encrypted = EncryptInt64(longValue.Value, key, settings.Algorithm);
                                var index = encryptedConstants.Count;
                                encryptedConstants.Add(new EncryptedConstant(index, encrypted, ConstantType.Int64));

                                SetLdcI4(instr, index);
                                instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptInt64));
                                i++;
                                modified = true;
                                stats.ConstantsEncrypted++;
                                continue;
                            }
                        }

                        // Try Single (float) constants
                        if (settings.EncryptFloats)
                        {
                            var floatValue = ExtractSingleValue(instr);
                            if (floatValue.HasValue && !ShouldSkipSingle(floatValue.Value, settings))
                            {
                                var encrypted = EncryptSingle(floatValue.Value, key, settings.Algorithm);
                                var index = encryptedConstants.Count;
                                encryptedConstants.Add(new EncryptedConstant(index, encrypted, ConstantType.Single));

                                SetLdcI4(instr, index);
                                instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptSingle));
                                i++;
                                modified = true;
                                stats.ConstantsEncrypted++;
                                continue;
                            }
                        }

                        // Try Double constants
                        if (settings.EncryptDoubles)
                        {
                            var doubleValue = ExtractDoubleValue(instr);
                            if (doubleValue.HasValue && !ShouldSkipDouble(doubleValue.Value, settings))
                            {
                                var encrypted = EncryptDouble(doubleValue.Value, key, settings.Algorithm);
                                var index = encryptedConstants.Count;
                                encryptedConstants.Add(new EncryptedConstant(index, encrypted, ConstantType.Double));

                                SetLdcI4(instr, index);
                                instructions.Insert(i + 1, Instruction.Create(OpCodes.Call, decryptDouble));
                                i++;
                                modified = true;
                                stats.ConstantsEncrypted++;
                                continue;
                            }
                        }
                    }

                    // Fix branch targets and instruction offsets after modifications
                    if (modified)
                    {
                        body.SimplifyBranches();
                        body.OptimizeBranches();

                        if (body.InitLocals == false && body.Variables.Count > 0)
                            body.InitLocals = true;

                        body.UpdateInstructionOffsets();
                    }
                }
            }

            // Store encrypted constants in the decryptor type
            StoreEncryptedConstants(decryptorType, encryptedConstants);

            _logger.LogInformation("Encrypted {Count} constants", stats.ConstantsEncrypted);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Constant encryption failed");
            return Task.FromResult(ObfuscationResult.Failed($"Constant encryption failed: {ex.Message}", ex));
        }
    }

    #region Value Extraction

    private static int? ExtractInt32Value(Instruction instr)
    {
        return instr.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4_S => (sbyte)instr.Operand,
            Code.Ldc_I4 => (int)instr.Operand,
            _ => null
        };
    }

    private static long? ExtractInt64Value(Instruction instr)
    {
        if (instr.OpCode == OpCodes.Ldc_I8)
            return (long)instr.Operand;
        return null;
    }

    private static float? ExtractSingleValue(Instruction instr)
    {
        if (instr.OpCode == OpCodes.Ldc_R4)
            return (float)instr.Operand;
        return null;
    }

    private static double? ExtractDoubleValue(Instruction instr)
    {
        if (instr.OpCode == OpCodes.Ldc_R8)
            return (double)instr.Operand;
        return null;
    }

    #endregion

    #region Skip Logic

    private static bool ShouldSkipInt32(int value, ConstantEncryptionSettings settings)
    {
        return Math.Abs(value) < settings.IntegerThreshold;
    }

    private static bool ShouldSkipInt64(long value, ConstantEncryptionSettings settings)
    {
        return Math.Abs(value) < settings.LongThreshold;
    }

    private static bool ShouldSkipSingle(float value, ConstantEncryptionSettings settings)
    {
        if (!settings.SkipCommonFloats)
            return false;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        return value == 0.0f || value == 1.0f || value == -1.0f;
    }

    private static bool ShouldSkipDouble(double value, ConstantEncryptionSettings settings)
    {
        if (!settings.SkipCommonDoubles)
            return false;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        return value == 0.0 || value == 1.0 || value == -1.0;
    }

    #endregion

    #region Encryption

    private static byte[] EncryptInt32(int value, byte[] key, EncryptionAlgorithm algorithm)
    {
        var bytes = BitConverter.GetBytes(value);
        return EncryptionHelper.EncryptBytes(bytes, key, algorithm);
    }

    private static byte[] EncryptInt64(long value, byte[] key, EncryptionAlgorithm algorithm)
    {
        var bytes = BitConverter.GetBytes(value);
        return EncryptionHelper.EncryptBytes(bytes, key, algorithm);
    }

    private static byte[] EncryptSingle(float value, byte[] key, EncryptionAlgorithm algorithm)
    {
        var bytes = BitConverter.GetBytes(value);
        return EncryptionHelper.EncryptBytes(bytes, key, algorithm);
    }

    private static byte[] EncryptDouble(double value, byte[] key, EncryptionAlgorithm algorithm)
    {
        var bytes = BitConverter.GetBytes(value);
        return EncryptionHelper.EncryptBytes(bytes, key, algorithm);
    }

    #endregion

    #region Decryptor Injection

    private TypeDef InjectDecryptorType(ModuleDef module, byte[] key, EncryptionAlgorithm algorithm)
    {
        // Create internal static class for decryption
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<ConstantDecryptor>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        // Add key field
        var keyField = new FieldDefUser(
            "_k",
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(keyField);

        // Add encrypted data field (byte[][])
        var byteArrayType = new SZArraySig(module.CorLibTypes.Byte);
        var dataField = new FieldDefUser(
            "_d",
            new FieldSig(new SZArraySig(byteArrayType)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(dataField);

        // Add decrypt methods for each type
        typeDef.Methods.Add(CreateDecryptInt32Method(module, keyField, dataField));
        typeDef.Methods.Add(CreateDecryptInt64Method(module, keyField, dataField));
        typeDef.Methods.Add(CreateDecryptSingleMethod(module, keyField, dataField));
        typeDef.Methods.Add(CreateDecryptDoubleMethod(module, keyField, dataField));

        // Add static constructor to initialize key
        var cctor = CreateStaticConstructor(module, keyField, key);
        typeDef.Methods.Add(cctor);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateDecryptInt32Method(ModuleDef module, FieldDef keyField, FieldDef dataField)
    {
        var method = new MethodDefUser(
            "DecryptInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        method.Body = body;

        // Local for decrypted bytes
        var decryptedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(decryptedLocal);

        // Get BitConverter.ToInt32 reference
        var bitConverterRef = new TypeRefUser(module, "System", "BitConverter", module.CorLibTypes.AssemblyRef);
        var toInt32 = new MemberRefUser(
            module,
            "ToInt32",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.Int32),
            bitConverterRef);

        // Inline XOR decryption:
        // byte[] encrypted = _d[index];
        // byte[] decrypted = new byte[encrypted.Length];
        // for (int i = 0; i < encrypted.Length; i++)
        //     decrypted[i] = (byte)(encrypted[i] ^ _k[i % _k.Length]);
        // return BitConverter.ToInt32(decrypted, 0);

        // For simplicity, we'll implement a direct XOR decryption
        // Load _d[index]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));

        // Create decrypted array of same length (4 bytes for int)
        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, decryptedLocal));

        // XOR each byte: decrypted[i] = encrypted[i] ^ key[i % key.Length]
        for (int i = 0; i < 4; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
            body.Instructions.Add(Instruction.CreateLdcI4(i));

            // Load encrypted[i]
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            // Load key[i % key.Length]
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Rem));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            // XOR
            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        // Return BitConverter.ToInt32(decrypted, 0)
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toInt32));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    private MethodDef CreateDecryptInt64Method(ModuleDef module, FieldDef keyField, FieldDef dataField)
    {
        var method = new MethodDefUser(
            "DecryptInt64",
            MethodSig.CreateStatic(module.CorLibTypes.Int64, module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        method.Body = body;

        var decryptedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(decryptedLocal);

        var bitConverterRef = new TypeRefUser(module, "System", "BitConverter", module.CorLibTypes.AssemblyRef);
        var toInt64 = new MemberRefUser(
            module,
            "ToInt64",
            MethodSig.CreateStatic(module.CorLibTypes.Int64, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.Int32),
            bitConverterRef);

        // Load _d[index]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));

        // Create decrypted array (8 bytes for long)
        body.Instructions.Add(Instruction.CreateLdcI4(8));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, decryptedLocal));

        // XOR each byte
        for (int i = 0; i < 8; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
            body.Instructions.Add(Instruction.CreateLdcI4(i));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Rem));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toInt64));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    private MethodDef CreateDecryptSingleMethod(ModuleDef module, FieldDef keyField, FieldDef dataField)
    {
        var method = new MethodDefUser(
            "DecryptSingle",
            MethodSig.CreateStatic(module.CorLibTypes.Single, module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        method.Body = body;

        var decryptedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(decryptedLocal);

        var bitConverterRef = new TypeRefUser(module, "System", "BitConverter", module.CorLibTypes.AssemblyRef);
        var toSingle = new MemberRefUser(
            module,
            "ToSingle",
            MethodSig.CreateStatic(module.CorLibTypes.Single, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.Int32),
            bitConverterRef);

        // Load _d[index]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));

        // Create decrypted array (4 bytes for float)
        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, decryptedLocal));

        // XOR each byte
        for (int i = 0; i < 4; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
            body.Instructions.Add(Instruction.CreateLdcI4(i));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Rem));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toSingle));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    private MethodDef CreateDecryptDoubleMethod(ModuleDef module, FieldDef keyField, FieldDef dataField)
    {
        var method = new MethodDefUser(
            "DecryptDouble",
            MethodSig.CreateStatic(module.CorLibTypes.Double, module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        method.Body = body;

        var decryptedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(decryptedLocal);

        var bitConverterRef = new TypeRefUser(module, "System", "BitConverter", module.CorLibTypes.AssemblyRef);
        var toDouble = new MemberRefUser(
            module,
            "ToDouble",
            MethodSig.CreateStatic(module.CorLibTypes.Double, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.Int32),
            bitConverterRef);

        // Load _d[index]
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));

        // Create decrypted array (8 bytes for double)
        body.Instructions.Add(Instruction.CreateLdcI4(8));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, decryptedLocal));

        // XOR each byte
        for (int i = 0; i < 8; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
            body.Instructions.Add(Instruction.CreateLdcI4(i));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, dataField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Rem));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, decryptedLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, toDouble));
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

    private void StoreEncryptedConstants(TypeDef decryptorType, List<EncryptedConstant> constants)
    {
        var dataField = decryptorType.FindField("_d");
        var cctor = decryptorType.FindMethod(".cctor");

        if (cctor?.Body == null || dataField == null)
            return;

        var module = decryptorType.Module;
        var body = cctor.Body;

        // Remove the final ret instruction
        if (body.Instructions.Count > 0 && body.Instructions[^1].OpCode == OpCodes.Ret)
        {
            body.Instructions.RemoveAt(body.Instructions.Count - 1);
        }

        // Initialize data array (byte[][])
        body.Instructions.Add(Instruction.CreateLdcI4(constants.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, new SZArraySig(module.CorLibTypes.Byte).ToTypeDefOrRef()));

        foreach (var constant in constants)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(constant.Index));

            // Create byte array for this constant
            body.Instructions.Add(Instruction.CreateLdcI4(constant.EncryptedBytes.Length));
            body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));

            // Fill the byte array
            for (int i = 0; i < constant.EncryptedBytes.Length; i++)
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Dup));
                body.Instructions.Add(Instruction.CreateLdcI4(i));
                body.Instructions.Add(Instruction.CreateLdcI4(constant.EncryptedBytes[i]));
                body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
            }

            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, dataField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
    }

    #endregion

    #region Exclusion Logic

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

    private static bool IsCompilerGenerated(TypeDef type)
    {
        if (type.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;

        var name = type.Name.String;
        if (name.StartsWith("<") || name.Contains(">d__") || name.Contains(">c__") ||
            name.Contains("<>c") || name.Contains("DisplayClass"))
            return true;

        if (type.Interfaces.Any(i => i.Interface.FullName == "System.Runtime.CompilerServices.IAsyncStateMachine"))
            return true;

        return false;
    }

    private static bool IsCompilerGeneratedMethod(MethodDef method)
    {
        if (method.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;

        var name = method.Name.String;
        if (name.StartsWith("<") || name.Contains(">b__") || name.Contains(">g__"))
            return true;

        if (method.IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_")))
        {
            if (method.Body?.Instructions.Count > 0)
            {
                var instructions = method.Body.Instructions;
                for (int i = 0; i < instructions.Count; i++)
                {
                    if (instructions[i].OpCode.FlowControl == FlowControl.Branch ||
                        instructions[i].OpCode.FlowControl == FlowControl.Cond_Branch)
                    {
                        if (instructions[i].Operand is Instruction target)
                        {
                            var targetIndex = instructions.IndexOf(target);
                            if (targetIndex < i)
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    #endregion

    #region Instruction Modification

    /// <summary>
    /// Modifies an instruction IN PLACE to become an ldc.i4 instruction.
    /// This preserves branch target references that point to this instruction.
    /// </summary>
    private static void SetLdcI4(Instruction instruction, int value)
    {
        switch (value)
        {
            case -1:
                instruction.OpCode = OpCodes.Ldc_I4_M1;
                instruction.Operand = null;
                break;
            case 0:
                instruction.OpCode = OpCodes.Ldc_I4_0;
                instruction.Operand = null;
                break;
            case 1:
                instruction.OpCode = OpCodes.Ldc_I4_1;
                instruction.Operand = null;
                break;
            case 2:
                instruction.OpCode = OpCodes.Ldc_I4_2;
                instruction.Operand = null;
                break;
            case 3:
                instruction.OpCode = OpCodes.Ldc_I4_3;
                instruction.Operand = null;
                break;
            case 4:
                instruction.OpCode = OpCodes.Ldc_I4_4;
                instruction.Operand = null;
                break;
            case 5:
                instruction.OpCode = OpCodes.Ldc_I4_5;
                instruction.Operand = null;
                break;
            case 6:
                instruction.OpCode = OpCodes.Ldc_I4_6;
                instruction.Operand = null;
                break;
            case 7:
                instruction.OpCode = OpCodes.Ldc_I4_7;
                instruction.Operand = null;
                break;
            case 8:
                instruction.OpCode = OpCodes.Ldc_I4_8;
                instruction.Operand = null;
                break;
            default:
                if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                {
                    instruction.OpCode = OpCodes.Ldc_I4_S;
                    instruction.Operand = (sbyte)value;
                }
                else
                {
                    instruction.OpCode = OpCodes.Ldc_I4;
                    instruction.Operand = value;
                }
                break;
        }
    }

    #endregion
}
