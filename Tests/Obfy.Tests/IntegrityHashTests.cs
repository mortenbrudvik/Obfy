using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class IntegrityHashTests
{
    [Fact]
    public void PatchIntegrityHash_StoresDigestThatMatchesZeroedFile()
    {
        var blob = new byte[AssemblyHashComputer.BlobSize];
        Buffer.BlockCopy(AssemblyHashComputer.Magic, 0, blob, 0, AssemblyHashComputer.Magic.Length);

        var file = new byte[256];
        new Random(1).NextBytes(file);
        Buffer.BlockCopy(blob, 0, file, 40, blob.Length);

        var path = Path.Combine(Path.GetTempPath(), $"obfy-hash-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, file);
            AssemblyHashComputer.PatchIntegrityHash(path);

            var patched = File.ReadAllBytes(path);
            var offset = AssemblyHashComputer.FindHashOffset(patched);
            offset.ShouldBe(40 + AssemblyHashComputer.Magic.Length);

            var stored = patched.AsSpan(offset, AssemblyHashComputer.HashSize).ToArray();
            var recomputed = AssemblyHashComputer.ComputeIntegrityHash(patched, offset);
            stored.ShouldBe(recomputed);

            patched[0] ^= 0xFF;
            AssemblyHashComputer.ComputeIntegrityHash(patched, offset).ShouldNotBe(stored);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Magic_IsNotPlainAsciiFingerprint()
    {
        var ascii = System.Text.Encoding.ASCII.GetString(AssemblyHashComputer.Magic);
        ascii.ShouldNotContain("OBFY");
        AssemblyHashComputer.Magic.ShouldContain(b => b > 0x7F);
    }
}
