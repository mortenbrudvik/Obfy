using System.Security.Cryptography;
using System.Text;

namespace Obfy.Core.Utilities;

/// <summary>
/// Computes and patches assembly hashes for anti-tamper protection.
/// The integrity hash covers the whole file with the 32-byte hash slot zeroed,
/// so storing the hash inside the file does not change the digest.
/// </summary>
public static class AssemblyHashComputer
{
    /// <summary>
    /// 16-byte marker that precedes the hash slot in the PE image.
    /// </summary>
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("OBFY_AT_MAGIC!!");

    public const int HashSize = 32;
    public const int BlobSize = 16 + HashSize;

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
    /// SHA-256 of <paramref name="fileBytes"/> with the hash slot zeroed.
    /// </summary>
    public static byte[] ComputeIntegrityHash(byte[] fileBytes, int hashOffset)
    {
        var copy = (byte[])fileBytes.Clone();
        Array.Clear(copy, hashOffset, HashSize);
        return SHA256.HashData(copy);
    }

    /// <summary>
    /// Locates the magic blob, hashes the file with the hash slot zeroed, and writes the digest into the slot.
    /// </summary>
    public static void PatchIntegrityHash(string assemblyPath)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        var hashOffset = FindHashOffset(bytes);
        if (hashOffset < 0)
            throw new InvalidOperationException("Anti-tamper magic blob was not found; cannot patch integrity hash.");

        var hash = ComputeIntegrityHash(bytes, hashOffset);
        Buffer.BlockCopy(hash, 0, bytes, hashOffset, HashSize);
        File.WriteAllBytes(assemblyPath, bytes);
    }

    /// <summary>
    /// Computes SHA-256 of the whole file. Prefer <see cref="ComputeIntegrityHash"/> for anti-tamper.
    /// </summary>
    public static byte[] ComputeAssemblyHash(string assemblyPath)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        return SHA256.HashData(bytes);
    }
}
