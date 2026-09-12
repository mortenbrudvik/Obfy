using System.Security.Cryptography;

namespace Obfy.Core.Utilities;

/// <summary>
/// Computes and patches assembly hashes for anti-tamper protection.
/// The integrity hash covers the whole file with the 32-byte hash slot and the
/// strong-name signature blob zeroed, so storing the hash and re-signing after
/// the hash does not change the digest.
/// </summary>
public static class AssemblyHashComputer
{
    /// <summary>
    /// 16-byte marker that precedes the hash slot in the PE image.
    /// Non-ASCII so it is not a searchable "OBFY" fingerprint.
    /// </summary>
    public static readonly byte[] Magic =
    [
        0xA7, 0x3C, 0x91, 0xD4, 0x5E, 0xB2, 0x88, 0xE2,
        0x9C, 0xA8, 0x01, 0xF3, 0x6D, 0x4A, 0xC1, 0x7B
    ];

    public const int HashSize = 32;
    public const int StrongNameRangeSize = 8;
    public const int BlobSize = 16 + HashSize + StrongNameRangeSize;

    /// <summary>
    /// Finds the magic marker and returns the file offset of the 32-byte hash slot.
    /// </summary>
    public static int FindHashOffset(byte[] fileBytes)
    {
        var magic = Magic;
        var max = fileBytes.Length - BlobSize;
        for (var i = 0; i <= max; i++)
        {
            var match = true;
            for (var j = 0; j < magic.Length; j++)
            {
                if (fileBytes[i + j] != magic[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i + magic.Length;
        }

        return -1;
    }

    /// <summary>
    /// SHA-256 of <paramref name="fileBytes"/> with the hash slot and strong-name signature zeroed.
    /// </summary>
    public static byte[] ComputeIntegrityHash(byte[] fileBytes, int hashOffset)
    {
        var copy = (byte[])fileBytes.Clone();
        Array.Clear(copy, hashOffset, HashSize);
        ZeroStrongNameSignature(copy);
        return SHA256.HashData(copy);
    }

    /// <summary>
    /// Locates the magic blob, records the strong-name signature range, hashes the file with
    /// the hash slot and SN blob zeroed, and writes the digest into the slot.
    /// </summary>
    public static void PatchIntegrityHash(string assemblyPath)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        var hashOffset = FindHashOffset(bytes);
        if (hashOffset < 0)
            throw new InvalidOperationException("Anti-tamper magic blob was not found; cannot patch integrity hash.");

        if (TryGetStrongNameSignatureRange(bytes, out var snOffset, out var snSize))
        {
            Buffer.BlockCopy(BitConverter.GetBytes(snOffset), 0, bytes, hashOffset + HashSize, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(snSize), 0, bytes, hashOffset + HashSize + 4, 4);
        }

        var hash = ComputeIntegrityHash(bytes, hashOffset);
        Buffer.BlockCopy(hash, 0, bytes, hashOffset, HashSize);
        File.WriteAllBytes(assemblyPath, bytes);
    }

    public static void ZeroStrongNameSignature(byte[] fileBytes)
    {
        if (!TryGetStrongNameSignatureRange(fileBytes, out var offset, out var size))
            return;
        Array.Clear(fileBytes, offset, size);
    }

    public static bool TryGetStrongNameSignatureRange(byte[] pe, out int offset, out int size)
    {
        offset = 0;
        size = 0;
        if (pe.Length < 0x40 || pe[0] != (byte)'M' || pe[1] != (byte)'Z')
            return false;

        var lfanew = BitConverter.ToInt32(pe, 0x3C);
        if (lfanew < 0 || lfanew + 26 > pe.Length)
            return false;
        if (pe[lfanew] != (byte)'P' || pe[lfanew + 1] != (byte)'E')
            return false;

        var numberOfSections = BitConverter.ToUInt16(pe, lfanew + 6);
        var sizeOfOptional = BitConverter.ToUInt16(pe, lfanew + 20);
        var opt = lfanew + 24;
        if (opt + 2 > pe.Length)
            return false;

        var magic = BitConverter.ToUInt16(pe, opt);
        var dd = magic == 0x20B ? opt + 112 : magic == 0x10B ? opt + 96 : -1;
        if (dd < 0 || dd + 15 * 8 > pe.Length)
            return false;

        var comRva = BitConverter.ToInt32(pe, dd + 14 * 8);
        if (comRva == 0)
            return false;

        var sectionStart = opt + sizeOfOptional;
        if (!TryRvaToOffset(pe, sectionStart, numberOfSections, comRva, out var comOff))
            return false;
        if (comOff + 40 > pe.Length)
            return false;

        var snRva = BitConverter.ToInt32(pe, comOff + 32);
        size = BitConverter.ToInt32(pe, comOff + 36);
        if (snRva == 0 || size <= 0)
            return false;
        if (!TryRvaToOffset(pe, sectionStart, numberOfSections, snRva, out offset))
            return false;
        return offset >= 0 && offset + size <= pe.Length;
    }

    private static bool TryRvaToOffset(byte[] pe, int sectionStart, int numberOfSections, int rva, out int offset)
    {
        offset = 0;
        for (var i = 0; i < numberOfSections; i++)
        {
            var rec = sectionStart + i * 40;
            if (rec + 24 > pe.Length)
                return false;
            var va = BitConverter.ToInt32(pe, rec + 12);
            var rawSize = BitConverter.ToInt32(pe, rec + 16);
            var rawPtr = BitConverter.ToInt32(pe, rec + 20);
            if (rva >= va && rva < va + Math.Max(rawSize, 1))
            {
                offset = rawPtr + (rva - va);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes SHA-256 of the whole file. Prefer <see cref="ComputeIntegrityHash"/> for anti-tamper.
    /// </summary>
    public static byte[] ComputeAssemblyHash(string assemblyPath)
    {
        return SHA256.HashData(File.ReadAllBytes(assemblyPath));
    }
}
