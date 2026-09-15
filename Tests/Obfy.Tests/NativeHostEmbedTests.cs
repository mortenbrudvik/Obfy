using System.Reflection.PortableExecutable;
using System.Text;
using Obfy.Core.Pipeline;
using Shouldly;

namespace Obfy.Tests;

public class NativeHostEmbedTests
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";
    public static readonly byte[] Sentinel = "OBPYOVL1"u8.ToArray();

    [Fact]
    public void EmbeddedResource_IsPresentOnObfyCore()
    {
        typeof(PipelineContext).Assembly
            .GetManifestResourceNames()
            .ShouldContain(EmbeddedName);
    }

    [Fact]
    public void EmbeddedHost_IsPeWithoutClrDirectory_AndHasSingleSentinel()
    {
        using var stream = typeof(PipelineContext).Assembly
            .GetManifestResourceStream(EmbeddedName);
        stream.ShouldNotBeNull();
        using var ms = new MemoryStream();
        stream!.CopyTo(ms);
        var bytes = ms.ToArray();

        bytes[0].ShouldBe((byte)'M');
        bytes[1].ShouldBe((byte)'Z');

        using var pe = new PEReader(new MemoryStream(bytes));
        pe.PEHeaders.PEHeader.ShouldNotBeNull();
        pe.PEHeaders.PEHeader!.Magic.ShouldBe(PEMagic.PE32Plus);
        pe.PEHeaders.CorHeader.ShouldBeNull();
        pe.PEHeaders.PEHeader.Subsystem.ShouldBe(Subsystem.WindowsCui);

        CountOccurrences(bytes, Sentinel).ShouldBe(1);
    }

    internal static int CountOccurrences(byte[] haystack, byte[] needle)
    {
        var n = 0;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                n++;
        }
        return n;
    }
}
