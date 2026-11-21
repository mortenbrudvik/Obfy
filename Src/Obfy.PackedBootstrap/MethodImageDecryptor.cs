using System;
using System.Buffers.Binary;

namespace Obfy.Runtime;

/// <summary>
/// XOR-decrypts method bodies in a PE image before <c>AssemblyLoadContext.LoadFromStream</c>.
/// A stream load has no mapped module (<c>Marshal.GetHINSTANCE</c> is -1), so the in-assembly
/// decryptor returns without touching the ciphertext and the entry point never runs.
/// This walks the same blob <c>MethodBodyPeEncryptor</c> writes. After a successful pass the
/// method count is set to 0, so a later pass — including one that does see a real module
/// base — does not XOR the plaintext again.
/// </summary>
internal static class MethodImageDecryptor
{
    internal const int MagicLength = 16;
    internal const int HeaderBytes = 8;
    internal const int EntryBytes = 16;
    internal const int KeyOffset = 12;

    private static readonly byte[] MagicBytes = new byte[]
    {
        0xB1, 0x6E, 0x4A, 0xC8, 0x03, 0xF5, 0x9D, 0x2A,
        0x77, 0xE0, 0x14, 0x8C, 0x5B, 0xD3, 0xA6, 0x19
    };

    internal static ReadOnlySpan<byte> Magic => MagicBytes;

    internal static void Decrypt(byte[] pe)
    {
        ArgumentNullException.ThrowIfNull(pe);
        var magicAt = FindMagic(pe);
        if (magicAt < 0)
            return;

        var header = magicAt + MagicLength;
        if (header + HeaderBytes > pe.Length)
            throw new InvalidOperationException("Method-encryption blob header is truncated.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(header));
        if (count == 0)
            return;
        if (count < 0)
            throw new InvalidOperationException("Method-encryption blob has a negative method count.");

        var entry = header + HeaderBytes;
        for (var i = 0; i < count; i++)
        {
            if (entry + EntryBytes > pe.Length)
                throw new InvalidOperationException("Method-encryption blob is shorter than its method count.");

            var rva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry));
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(entry + 4));
            var ilSize = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(entry + 8));
            var key = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(entry + KeyOffset));
            entry += EntryBytes;

            if (rva == 0 || ilSize <= 0 || headerSize <= 0 || key == 0)
                continue;

            var fileOffset = RvaToOffset(pe, rva);
            if (fileOffset < 0)
                throw new InvalidOperationException($"Could not map method RVA 0x{rva:X}.");

            var ilOffset = fileOffset + headerSize;
            if (ilOffset < 0 || ilOffset + ilSize > pe.Length)
                throw new InvalidOperationException($"Method body at RVA 0x{rva:X} exceeds the PE image.");

            var keyByte = (byte)key;
            for (var b = 0; b < ilSize; b++)
                pe[ilOffset + b] ^= keyByte;
        }

        BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(header), 0);
    }

    private static int FindMagic(byte[] pe)
    {
        var magic = Magic;
        var max = pe.Length - (magic.Length + HeaderBytes);
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

            if (match)
                return i;
        }

        return -1;
    }

    private static int RvaToOffset(byte[] pe, uint rva)
    {
        if (pe.Length < 0x40)
            return -1;
        var eLfanew = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x3C));
        if (eLfanew < 0 || eLfanew + 24 + 2 + 2 + 16 > pe.Length)
            return -1;

        var numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(eLfanew + 6));
        var sizeOfOptional = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(eLfanew + 20));
        var sectionStart = eLfanew + 24 + sizeOfOptional;
        const int sectionSize = 40;

        for (var i = 0; i < numberOfSections; i++)
        {
            var s = sectionStart + i * sectionSize;
            if (s + sectionSize > pe.Length)
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
}
