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

        var methodsByFullName = IndexMethods(loaded);
        var entries = new List<(uint Rva, int HeaderSize, int IlSize, byte Key)>(metadata.Entries.Count);
        foreach (var item in metadata.Entries)
        {
            methodsByFullName.TryGetValue(item.Method.FullName, out var match);
            if (match is null || match.RVA == 0 || match.Body is null)
                throw new InvalidOperationException($"Could not map {item.Method.FullName} for IL encryption.");

            var rva = (uint)match.RVA;
            var fileOffset = RvaToOffset(bytes, rva);
            if (fileOffset < 0 || fileOffset >= bytes.Length)
                throw new InvalidOperationException($"Could not map RVA 0x{rva:X} for {item.Method.FullName}.");

            if (!TryReadMethodBodyLayout(bytes, fileOffset, out var headerSize, out var ilSize))
                throw new InvalidOperationException($"Could not read method body layout for {item.Method.FullName}.");

            var ilOffset = (long)fileOffset + headerSize;
            if (ilOffset + ilSize > bytes.Length)
                throw new InvalidOperationException($"Method body for {item.Method.FullName} exceeds the PE image.");

            for (var i = 0; i < ilSize; i++)
                bytes[(int)ilOffset + i] ^= item.Key;

            entries.Add((rva, headerSize, ilSize, item.Key));
        }

        if (entries.Count != metadata.Entries.Count)
            throw new InvalidOperationException("Method-encryption PE rewrite dropped one or more methods.");

        PatchBlob(bytes, entries);
        File.WriteAllBytes(assemblyPath, bytes);
    }

    // One pass over the written module; the first method with a given full name wins, as the
    // per-entry scan this replaces did. FullName is expensive, so it is computed once per method.
    private static Dictionary<string, MethodDef> IndexMethods(ModuleDef module)
    {
        var index = new Dictionary<string, MethodDef>(StringComparer.Ordinal);
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
                index.TryAdd(method.FullName, method);
        }

        return index;
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
                if ((long)fileOffset + 12 > pe.Length)
                    return false;
                headerSize = ((pe[fileOffset + 1] >> 4) & 0xF) * 4;
                if (headerSize < 12 || (long)fileOffset + headerSize > pe.Length)
                    return false;
                ilSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(fileOffset + 4));
                break;
            default:
                return false;
        }

        return ilSize > 0 && (long)fileOffset + headerSize + ilSize <= pe.Length;
    }

    private static int RvaToOffset(byte[] pe, uint rva)
    {
        if (pe.Length < 0x40)
            return -1;
        var eLfanew = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        if (eLfanew < 0 || (long)eLfanew + 24 > pe.Length)
            return -1;

        var peOff = eLfanew;
        var numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(peOff + 6));
        var sizeOfOptional = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(peOff + 20));
        var sectionStart = (long)peOff + 24 + sizeOfOptional;
        if (sectionStart < 0 || sectionStart > pe.Length)
            return -1;
        const int sectionSize = 40;

        for (var i = 0; i < numberOfSections; i++)
        {
            var sLong = sectionStart + (long)i * sectionSize;
            if (sLong + sectionSize > pe.Length)
                break;
            var s = (int)sLong;
            var virtAddr = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 12));
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 16));
            var rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(s + 20));
            if (rva >= virtAddr && (ulong)(rva - virtAddr) < rawSize)
            {
                var offset = (ulong)rawPtr + (ulong)(rva - virtAddr);
                return offset < (ulong)pe.Length ? (int)offset : -1;
            }
        }

        return -1;
    }

    private static void PatchBlob(byte[] pe, List<(uint Rva, int HeaderSize, int IlSize, byte Key)> entries)
    {
        var magic = MethodEncryptionMetadata.Magic;
        var foundAt = -1;
        for (var i = 0; i <= pe.Length - magic.Length; i++)
        {
            if (!pe.AsSpan(i, magic.Length).SequenceEqual(magic))
                continue;

            if (foundAt >= 0)
                throw new InvalidOperationException("Method-encryption blob marker appeared more than once; cannot patch RVAs.");

            foundAt = i;
        }

        if (foundAt < 0)
            throw new InvalidOperationException("Method-encryption blob was not found; cannot patch RVAs.");

        var headerOffset = (long)foundAt + magic.Length;
        var requiredBytes = MethodEncryptionMetadata.HeaderBytes + (long)entries.Count * MethodEncryptionMetadata.EntryBytes;
        if (headerOffset + requiredBytes > pe.Length)
            throw new InvalidOperationException("Method-encryption blob is too small to store every entry.");

        var offset = (int)headerOffset;
        BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset), entries.Count);
        offset += MethodEncryptionMetadata.HeaderBytes;
        foreach (var (rva, header, size, key) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(offset), rva);
            BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + 4), header);
            BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + 8), size);
            BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(offset + MethodEncryptionMetadata.KeyOffset), key);
            offset += MethodEncryptionMetadata.EntryBytes;
        }
    }
}
