using System.Buffers.Binary;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// XOR-encrypts method IL in a written PE and patches the runtime decryptor blob with RVAs.
/// </summary>
internal static class MethodBodyPeEncryptor
{
    public static void Encrypt(string assemblyPath, MethodEncryptionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var bytes = File.ReadAllBytes(assemblyPath);
        using var loaded = ModuleDefMD.Load(bytes);

        var entries = new List<(uint Rva, int HeaderSize, int IlSize, byte Key)>(metadata.Entries.Count);
        foreach (var item in metadata.Entries)
        {
            var match = FindMethod(loaded, item.Method);
            if (match is null || match.RVA == 0 || match.Body is null)
                throw new InvalidOperationException($"Could not map {item.Method.FullName} for IL encryption.");

            var rva = (uint)match.RVA;
            var fileOffset = RvaToOffset(bytes, rva);
            if (fileOffset < 0 || fileOffset >= bytes.Length)
                throw new InvalidOperationException($"Could not map RVA 0x{rva:X} for {item.Method.FullName}.");

            if (!TryReadMethodBodyLayout(bytes, fileOffset, out var headerSize, out var ilSize))
                throw new InvalidOperationException($"Could not read method body layout for {item.Method.FullName}.");

            var ilOffset = fileOffset + headerSize;
            if (ilOffset + ilSize > bytes.Length)
                throw new InvalidOperationException($"Method body for {item.Method.FullName} exceeds the PE image.");

            for (var i = 0; i < ilSize; i++)
                bytes[ilOffset + i] ^= item.Key;

            entries.Add((rva, headerSize, ilSize, item.Key));
        }

        if (entries.Count != metadata.Entries.Count)
            throw new InvalidOperationException("Method-encryption PE rewrite dropped one or more methods.");

        PatchBlob(bytes, entries);
        File.WriteAllBytes(assemblyPath, bytes);
    }

    private static MethodDef? FindMethod(ModuleDef module, MethodDef original)
    {
        var fullName = original.FullName;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.FullName == fullName)
                    return method;
            }
        }

        return null;
    }

    private static bool TryReadMethodBodyLayout(byte[] pe, int fileOffset, out int headerSize, out int ilSize)
    {
        headerSize = 0;
        ilSize = 0;
        if (fileOffset < 0 || fileOffset >= pe.Length)
            return false;

        var first = pe[fileOffset];
        switch (first & 3)
        {
            case 2: // Tiny
                headerSize = 1;
                ilSize = first >> 2;
                break;
            case 3: // Fat
                if (fileOffset + 12 > pe.Length)
                    return false;
                headerSize = 12;
                ilSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(fileOffset + 4));
                break;
            default:
                return false;
        }

        return ilSize > 0 && fileOffset + headerSize + ilSize <= pe.Length;
    }

    private static int RvaToOffset(byte[] pe, uint rva)
    {
        if (pe.Length < 0x40)
            return -1;
        var eLfanew = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        if (eLfanew < 0 || eLfanew + 24 + 2 + 2 + 16 > pe.Length)
            return -1;

        var peOff = eLfanew;
        var numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(peOff + 6));
        var sizeOfOptional = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(peOff + 20));
        var sectionStart = peOff + 24 + sizeOfOptional;
        const int sectionSize = 40;

        for (var i = 0; i < numberOfSections; i++)
        {
            var s = sectionStart + i * sectionSize;
            if (s + 40 > pe.Length)
                break;
            var virtAddr = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 12));
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 16));
            var rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 20));
            var virtSize = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 8));
            var span = Math.Max(rawSize, virtSize);
            if (rva >= virtAddr && rva < virtAddr + span)
                return (int)(rawPtr + (rva - virtAddr));
        }

        return -1;
    }

    private static void PatchBlob(byte[] pe, List<(uint Rva, int HeaderSize, int IlSize, byte Key)> entries)
    {
        var magic = MethodEncryptionMetadata.Magic;
        var max = pe.Length - (magic.Length + MethodEncryptionMetadata.HeaderBytes);
        for (var i = 0; i <= max; i++)
        {
            var match = true;
            for (var j = 0; j < magic.Length; j++)
            {
                if (pe[i + j] != magic[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            var offset = i + magic.Length;
            BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset), entries.Count);
            offset += MethodEncryptionMetadata.HeaderBytes;
            foreach (var (rva, header, size, key) in entries)
            {
                if (offset + MethodEncryptionMetadata.EntryBytes > pe.Length)
                    throw new InvalidOperationException("Method-encryption blob is too small to store every entry.");
                BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(offset), rva);
                BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + 4), header);
                BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + 8), size);
                BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + MethodEncryptionMetadata.KeyOffset), key);
                offset += MethodEncryptionMetadata.EntryBytes;
            }

            return;
        }

        throw new InvalidOperationException("Method-encryption blob was not found; cannot patch RVAs.");
    }
}
