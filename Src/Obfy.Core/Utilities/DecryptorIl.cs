using System.Buffers.Binary;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Emits runtime decryptor IL that matches <see cref="EncryptionHelper"/>.
/// </summary>
internal static class DecryptorIl
{
    public static MethodDef CreateXorDecryptBytes(ModuleDef module, string name, MethodAttributes attributes)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte)),
            attributes);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var resultLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var iLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(resultLocal);
        body.Variables.Add(iLocal);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        var loopStart = Instruction.Create(OpCodes.Ldloc, resultLocal);
        var loopCheck = Instruction.Create(OpCodes.Ldloc, iLocal);

        body.Instructions.Add(Instruction.Create(OpCodes.Br, loopCheck));

        body.Instructions.Add(loopStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Rem));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));

        body.Instructions.Add(Instruction.Create(OpCodes.Xor));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, iLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, iLocal));

        body.Instructions.Add(loopCheck);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loopStart));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        return method;
    }

    public static MethodDef CreateAesDecryptBytes(ModuleDef module, string name, MethodAttributes attributes)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte)),
            attributes);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var aesType = FrameworkReferences.Type(module, "System.Security.Cryptography", "Aes");
        var bufferType = FrameworkReferences.Type(module, "System", "Buffer");
        var disposableType = FrameworkReferences.Type(module, "System", "IDisposable");
        var transformType = FrameworkReferences.Type(module, "System.Security.Cryptography", "ICryptoTransform");

        var aesCreate = new MemberRefUser(module, "Create",
            MethodSig.CreateStatic(new ClassSig(aesType)), aesType);
        var setKey = new MemberRefUser(module, "set_Key",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Byte)), aesType);
        var setIv = new MemberRefUser(module, "set_IV",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Byte)), aesType);
        var createDecryptor = new MemberRefUser(module, "CreateDecryptor",
            MethodSig.CreateInstance(new ClassSig(transformType)), aesType);
        var transformFinal = new MemberRefUser(module, "TransformFinalBlock",
            MethodSig.CreateInstance(
                new SZArraySig(module.CorLibTypes.Byte),
                new SZArraySig(module.CorLibTypes.Byte),
                module.CorLibTypes.Int32,
                module.CorLibTypes.Int32),
            transformType);
        var arrayType = FrameworkReferences.Type(module, "System", "Array");
        var blockCopy = new MemberRefUser(module, "BlockCopy",
            MethodSig.CreateStatic(
                module.CorLibTypes.Void,
                new ClassSig(arrayType),
                module.CorLibTypes.Int32,
                new ClassSig(arrayType),
                module.CorLibTypes.Int32,
                module.CorLibTypes.Int32),
            bufferType);
        var dispose = new MemberRefUser(module, "Dispose",
            MethodSig.CreateInstance(module.CorLibTypes.Void), disposableType);

        var aesLocal = new Local(new ClassSig(aesType));
        var ivLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var cipherLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var transformLocal = new Local(new ClassSig(transformType));
        var resultLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(aesLocal);
        body.Variables.Add(ivLocal);
        body.Variables.Add(cipherLocal);
        body.Variables.Add(transformLocal);
        body.Variables.Add(resultLocal);

        // try { aes = Aes.Create(); aes.Key = key; ... result = transform.TransformFinalBlock(...); }
        // finally { transform?.Dispose(); aes?.Dispose(); }
        var tryStart = Instruction.Create(OpCodes.Call, aesCreate);
        var finallyStart = Instruction.Create(OpCodes.Ldloc, transformLocal);
        var checkAes = Instruction.Create(OpCodes.Ldloc, aesLocal);
        var endFinally = Instruction.Create(OpCodes.Endfinally);
        var returnResult = Instruction.Create(OpCodes.Ldloc, resultLocal);

        body.Instructions.Add(tryStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, setKey));

        // iv = new byte[16]; Buffer.BlockCopy(data, 0, iv, 0, 16); aes.IV = iv;
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ivLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ivLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ivLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, setIv));

        // cipher = new byte[data.Length - 16]; Buffer.BlockCopy(data, 16, cipher, 0, cipher.Length);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, cipherLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, cipherLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, cipherLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));

        // transform = aes.CreateDecryptor(); result = transform.TransformFinalBlock(cipher, 0, cipher.Length);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, createDecryptor));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, transformLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, transformLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, cipherLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, cipherLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, transformFinal));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Leave, returnResult));

        body.Instructions.Add(finallyStart);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, checkAes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, transformLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, dispose));

        body.Instructions.Add(checkAes);
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, endFinally));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, aesLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, dispose));
        body.Instructions.Add(endFinally);

        body.Instructions.Add(returnResult);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = finallyStart,
            HandlerStart = finallyStart,
            HandlerEnd = returnResult
        });

        body.UpdateInstructionOffsets();
        return method;
    }

    /// <summary>
    /// Builds a string from raw UTF-16 code units. <c>Encoding.Unicode.GetString</c> replaces a lone surrogate.
    /// </summary>
    public static MethodDef CreateUtf16ToString(ModuleDef module)
    {
        var method = new MethodDefUser(
            "Utf16",
            MethodSig.CreateStatic(module.CorLibTypes.String, new SZArraySig(module.CorLibTypes.Byte)),
            MethodAttributes.Private | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var bytes = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var chars = new Local(new SZArraySig(module.CorLibTypes.Char));
        var index = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(bytes);
        body.Variables.Add(chars);
        body.Variables.Add(index);

        var ctor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new SZArraySig(module.CorLibTypes.Char)),
            module.CorLibTypes.String.TypeDefOrRef);
        var loop = Instruction.Create(OpCodes.Ldloc, chars);
        var check = Instruction.Create(OpCodes.Ldloc, index);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, bytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Div));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Char.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, chars));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, index));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, check));

        body.Instructions.Add(loop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, index));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, index));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bytes));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, index));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_8));
        body.Instructions.Add(Instruction.Create(OpCodes.Shl));
        body.Instructions.Add(Instruction.Create(OpCodes.Or));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_U2));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I2));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, index));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, index));

        body.Instructions.Add(check);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, chars));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, chars));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        return method;
    }

    public static MethodDef CreateStringDecrypt(
        ModuleDef module,
        FieldDef keyField,
        FieldDef stringsField,
        FieldDef cacheField,
        FieldDef indexXorField,
        MethodDef bytesDecrypt,
        MethodDef utf16ToString,
        string name = "Decrypt")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.Int32),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        var resultLocal = new Local(module.CorLibTypes.String);
        var indexLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(resultLocal);
        body.Variables.Add(indexLocal);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, indexXorField));
        body.Instructions.Add(Instruction.Create(OpCodes.Xor));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, indexLocal));

        var decryptLabel = Instruction.Create(OpCodes.Nop);
        var storeLabel = Instruction.Create(OpCodes.Nop);

        // if (_c != null && _c[index] != null) return _c[index];
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, decryptLabel));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, storeLabel));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        body.Instructions.Add(decryptLabel);
        // result = Utf16(DecryptBytes(_s[index], _k))
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, stringsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, keyField));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, bytesDecrypt));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, utf16ToString));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, resultLocal));

        // if (_c == null) _c = new string[_s.Length];
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        var afterInitCache = Instruction.Create(OpCodes.Nop);
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, afterInitCache));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, stringsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, cacheField));
        body.Instructions.Add(afterInitCache);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, cacheField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.Instructions.Add(storeLabel);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }

    /// <summary>
    /// Emits IL that stores <paramref name="key"/> into <paramref name="keyField"/> after XOR-decoding
    /// each byte with <c>mask ^ index</c>, so the raw key bytes are not a contiguous literal dump.
    /// </summary>
    public static void EmitEncodedKey(CilBody body, ModuleDef module, FieldDef keyField, byte[] key)
    {
        var mask = (byte)((key.Length * 17 + 0x5A) & 0xFF);

        body.Instructions.Add(Instruction.CreateLdcI4(key.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));

        for (var i = 0; i < key.Length; i++)
        {
            var encoded = (byte)(key[i] ^ mask ^ (byte)i);
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.CreateLdcI4(encoded));
            body.Instructions.Add(Instruction.CreateLdcI4(mask));
            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Xor));
            body.Instructions.Add(Instruction.Create(OpCodes.Conv_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I1));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, keyField));
    }

    /// <summary>
    /// Emits IL that builds a <c>byte[][]</c> from <paramref name="blobs"/> and stores it in
    /// <paramref name="field"/>. The bytes live in one RVA field. A fixed loop copies them out,
    /// so the static constructor does not grow by one store per byte.
    /// </summary>
    public static void EmitByteArrayArray(CilBody body, ModuleDef module, FieldDef field, IReadOnlyList<byte[]> blobs)
    {
        var byteArrayArray = new SZArraySig(module.CorLibTypes.Byte).ToTypeDefOrRef();
        if (blobs.Count == 0)
        {
            body.Instructions.Add(Instruction.CreateLdcI4(0));
            body.Instructions.Add(Instruction.Create(OpCodes.Newarr, byteArrayArray));
            body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, field));
            return;
        }

        var packed = PackBlobs(blobs);
        var rvaField = CreateRvaField(module, field, packed);
        body.InitLocals = true;

        var packedLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var arraysLocal = new Local(new SZArraySig(new SZArraySig(module.CorLibTypes.Byte)));
        var indexLocal = new Local(module.CorLibTypes.Int32);
        var offsetLocal = new Local(module.CorLibTypes.Int32);
        var lengthLocal = new Local(module.CorLibTypes.Int32);
        var itemLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(packedLocal);
        body.Variables.Add(arraysLocal);
        body.Variables.Add(indexLocal);
        body.Variables.Add(offsetLocal);
        body.Variables.Add(lengthLocal);
        body.Variables.Add(itemLocal);

        var runtimeHelpers = FrameworkReferences.Type(module, "System.Runtime.CompilerServices", "RuntimeHelpers");
        var initArray = new MemberRefUser(
            module,
            "InitializeArray",
            MethodSig.CreateStatic(
                module.CorLibTypes.Void,
                new ClassSig(FrameworkReferences.Type(module, "System", "Array")),
                new ValueTypeSig(FrameworkReferences.Type(module, "System", "RuntimeFieldHandle"))),
            runtimeHelpers);
        var blockCopy = new MemberRefUser(
            module,
            "BlockCopy",
            MethodSig.CreateStatic(
                module.CorLibTypes.Void,
                new ClassSig(FrameworkReferences.Type(module, "System", "Array")),
                module.CorLibTypes.Int32,
                new ClassSig(FrameworkReferences.Type(module, "System", "Array")),
                module.CorLibTypes.Int32,
                module.CorLibTypes.Int32),
            FrameworkReferences.Type(module, "System", "Buffer"));

        body.Instructions.Add(Instruction.CreateLdcI4(packed.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, rvaField));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, initArray));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, packedLocal));

        body.Instructions.Add(Instruction.CreateLdcI4(blobs.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, byteArrayArray));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, arraysLocal));

        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, indexLocal));

        var loop = Instruction.Create(OpCodes.Ldloc, packedLocal);
        body.Instructions.Add(loop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        EmitLittleEndianInt32(body, packedLocal, offsetLocal);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, lengthLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(4));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, lengthLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, itemLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, packedLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, itemLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, lengthLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, blockCopy));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, lengthLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, arraysLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, itemLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, indexLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, indexLocal));
        body.Instructions.Add(Instruction.CreateLdcI4(blobs.Count));
        body.Instructions.Add(Instruction.Create(OpCodes.Blt, loop));

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, arraysLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, field));
    }

    private static byte[] PackBlobs(IReadOnlyList<byte[]> blobs)
    {
        var size = 4;
        foreach (var blob in blobs)
            size = checked(size + 4 + blob.Length);

        var packed = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(0, 4), blobs.Count);
        var offset = 4;
        foreach (var blob in blobs)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(offset, 4), blob.Length);
            offset += 4;
            blob.CopyTo(packed.AsSpan(offset));
            offset += blob.Length;
        }

        return packed;
    }

    private static FieldDef CreateRvaField(ModuleDef module, FieldDef destination, byte[] packed)
    {
        if (destination.DeclaringType is not TypeDef owner)
            throw new InvalidOperationException("Encrypted blob field has no declaring type.");

        var blobType = new TypeDefUser(
            owner.Namespace,
            "<Blob>" + owner.NestedTypes.Count,
            FrameworkReferences.Type(module, "System", "ValueType"))
        {
            Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout |
                         TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            ClassLayout = new ClassLayoutUser(1, (uint)packed.Length)
        };
        owner.NestedTypes.Add(blobType);

        var rvaField = new FieldDefUser(
            "_rva" + owner.Fields.Count,
            new FieldSig(new ValueTypeSig(blobType)),
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.HasFieldRVA)
        {
            InitialValue = packed
        };
        owner.Fields.Add(rvaField);
        return rvaField;
    }

    /// <summary>
    /// Pushes a little-endian int32 read from <paramref name="packed"/> at <paramref name="offset"/>.
    /// </summary>
    private static void EmitLittleEndianInt32(CilBody body, Local packed, Local offset)
    {
        // The loop label already pushed packed and offset for the first byte.
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        for (var shift = 8; shift <= 24; shift += 8)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, packed));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offset));
            body.Instructions.Add(Instruction.CreateLdcI4(shift / 8));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
            body.Instructions.Add(Instruction.CreateLdcI4(shift));
            body.Instructions.Add(Instruction.Create(OpCodes.Shl));
            body.Instructions.Add(Instruction.Create(OpCodes.Or));
        }
    }
}
