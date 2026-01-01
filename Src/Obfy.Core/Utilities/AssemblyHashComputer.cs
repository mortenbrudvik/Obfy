using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Computes and patches assembly hashes for anti-tamper protection.
/// </summary>
public static class AssemblyHashComputer
{
    /// <summary>
    /// Computes SHA-256 hash of an assembly file.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    public static byte[] ComputeAssemblyHash(string assemblyPath)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        return SHA256.HashData(bytes);
    }

    /// <summary>
    /// Computes SHA-256 hash of all method body IL bytes in a module,
    /// excluding the specified namespace (used to exclude the anti-tamper runtime type).
    /// </summary>
    /// <param name="module">The module to hash.</param>
    /// <param name="excludeNamespace">Namespace to exclude from hashing.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    public static byte[] ComputeMethodBodiesHash(ModuleDef module, string excludeNamespace)
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        foreach (var type in module.GetTypes())
        {
            // Skip excluded namespace (anti-tamper runtime type)
            if (!string.IsNullOrEmpty(type.Namespace) &&
                type.Namespace.StartsWith(excludeNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var method in type.Methods)
            {
                if (method.HasBody && method.Body.Instructions.Count > 0)
                {
                    // Write method token for uniqueness
                    var tokenBytes = BitConverter.GetBytes(method.MDToken.Raw);
                    stream.Write(tokenBytes, 0, tokenBytes.Length);

                    // Write each instruction's opcode and operand info
                    foreach (var instr in method.Body.Instructions)
                    {
                        // Write opcode value
                        var opcodeBytes = BitConverter.GetBytes((ushort)instr.OpCode.Value);
                        stream.Write(opcodeBytes, 0, opcodeBytes.Length);

                        // Write operand hash if present
                        if (instr.Operand != null)
                        {
                            var operandHash = GetOperandHash(instr.Operand);
                            stream.Write(operandHash, 0, operandHash.Length);
                        }
                    }
                }
            }
        }

        stream.Position = 0;
        return sha256.ComputeHash(stream);
    }

    private static byte[] GetOperandHash(object operand)
    {
        // Convert operand to a consistent byte representation
        return operand switch
        {
            int i => BitConverter.GetBytes(i),
            long l => BitConverter.GetBytes(l),
            float f => BitConverter.GetBytes(f),
            double d => BitConverter.GetBytes(d),
            string s => System.Text.Encoding.UTF8.GetBytes(s),
            IMemberRef member => BitConverter.GetBytes(member.MDToken.Raw),
            Local local => BitConverter.GetBytes(local.Index),
            Parameter param => BitConverter.GetBytes(param.Index),
            dnlib.DotNet.Emit.Instruction instr => BitConverter.GetBytes(instr.Offset),
            dnlib.DotNet.Emit.Instruction[] instrs => instrs.SelectMany(i => BitConverter.GetBytes(i.Offset)).ToArray(),
            _ => BitConverter.GetBytes(operand.GetHashCode())
        };
    }

    /// <summary>
    /// Finds and patches the hash placeholder in an assembly file.
    /// Searches for the pattern of 32 consecutive zero bytes in the static constructor
    /// of the AntiTamper type and replaces them with the actual hash.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <param name="expectedHash">The hash to patch into the placeholder.</param>
    /// <returns>True if patching succeeded, false otherwise.</returns>
    public static bool PatchHashPlaceholder(string assemblyPath, byte[] expectedHash)
    {
        if (expectedHash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes (SHA-256)", nameof(expectedHash));

        var bytes = File.ReadAllBytes(assemblyPath);

        // Find the placeholder pattern: 32 consecutive zero bytes
        // This is a simplified approach - in production, we'd use the field token
        // to locate the exact position more reliably
        var placeholderPattern = new byte[32];
        var placeholderIndex = FindPattern(bytes, placeholderPattern);

        if (placeholderIndex == -1)
        {
            // Try alternative: find the newarr + stsfld pattern and locate the data
            return false;
        }

        // Patch the placeholder with the actual hash
        Array.Copy(expectedHash, 0, bytes, placeholderIndex, 32);

        File.WriteAllBytes(assemblyPath, bytes);
        return true;
    }

    /// <summary>
    /// Patches the hash field in an assembly using the field's RVA.
    /// This is more reliable than pattern matching.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <param name="hashFieldToken">Metadata token of the hash field.</param>
    /// <param name="expectedHash">The hash to store.</param>
    public static void PatchHashFieldByToken(string assemblyPath, uint hashFieldToken, byte[] expectedHash)
    {
        if (expectedHash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes (SHA-256)", nameof(expectedHash));

        // Load the module to find the field's data location
        using var module = ModuleDefMD.Load(assemblyPath);

        // Find the AntiTamper type and its static constructor
        var antiTamperType = module.Types.FirstOrDefault(t =>
            t.Namespace == "Obfy.Runtime" && t.Name == "<AntiTamper>");

        if (antiTamperType == null)
            throw new InvalidOperationException("AntiTamper type not found in assembly");

        var cctor = antiTamperType.Methods.FirstOrDefault(m => m.IsStaticConstructor);
        if (cctor?.Body == null)
            throw new InvalidOperationException("AntiTamper static constructor not found");

        // The hash is initialized via newarr + stsfld
        // We need to find where in the file the array data would be stored
        // For a truly robust solution, we'd need to modify the IL to use InitializeArray
        // with a FieldRVA, but for now we use a simpler approach:
        // We modify the IL to load the hash bytes directly

        // Find the hash field
        var hashField = antiTamperType.Fields.FirstOrDefault(f => f.Name == "_h");
        if (hashField == null)
            throw new InvalidOperationException("Hash field not found");

        // Modify the static constructor to initialize with actual hash
        ModifyStaticConstructorHash(cctor, hashField, expectedHash, module);

        // Save the modified module
        module.Write(assemblyPath);
    }

    private static void ModifyStaticConstructorHash(MethodDef cctor, FieldDef hashField, byte[] hash, ModuleDef module)
    {
        var body = cctor.Body;
        var instructions = body.Instructions;

        // Find and replace the hash initialization sequence:
        // ldc.i4 32
        // newarr byte
        // stsfld _h
        //
        // Replace with:
        // ldc.i4 32
        // newarr byte
        // [dup + ldc.i4 index + ldc.i4 value + stelem.i1] x 32
        // stsfld _h

        // Find the stsfld instruction for the hash field
        int stsfldIndex = -1;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Stsfld &&
                instructions[i].Operand is FieldDef field &&
                field.Name == "_h")
            {
                stsfldIndex = i;
                break;
            }
        }

        if (stsfldIndex == -1)
            return;

        // Find the newarr instruction before stsfld
        int newarrIndex = -1;
        for (int i = stsfldIndex - 1; i >= 0; i--)
        {
            if (instructions[i].OpCode == dnlib.DotNet.Emit.OpCodes.Newarr)
            {
                newarrIndex = i;
                break;
            }
        }

        if (newarrIndex == -1)
            return;

        // Insert array initialization between newarr and stsfld
        var insertIndex = newarrIndex + 1;
        for (int i = 0; i < hash.Length; i++)
        {
            // dup
            instructions.Insert(insertIndex++, dnlib.DotNet.Emit.Instruction.Create(dnlib.DotNet.Emit.OpCodes.Dup));
            // ldc.i4 index
            instructions.Insert(insertIndex++, dnlib.DotNet.Emit.Instruction.CreateLdcI4(i));
            // ldc.i4 value
            instructions.Insert(insertIndex++, dnlib.DotNet.Emit.Instruction.CreateLdcI4(hash[i]));
            // stelem.i1
            instructions.Insert(insertIndex++, dnlib.DotNet.Emit.Instruction.Create(dnlib.DotNet.Emit.OpCodes.Stelem_I1));
        }

        body.UpdateInstructionOffsets();
    }

    private static int FindPattern(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return i;
        }
        return -1;
    }
}
