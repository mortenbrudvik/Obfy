using dnlib.DotNet;

namespace Obfy.Core.Models;

/// <summary>
/// One method body to XOR in the PE, with its own non-zero key.
/// </summary>
public readonly struct EncryptedMethodBody
{
    public EncryptedMethodBody(MethodDef method, byte key)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (key == 0)
            throw new ArgumentOutOfRangeException(nameof(key), "XOR key must be non-zero.");
        Method = method;
        Key = key;
    }

    public MethodDef Method { get; }
    public byte Key { get; }
}

/// <summary>
/// Method-body encryption state: methods to XOR in the PE after RVAs are assigned,
/// plus the magic blob the runtime decryptor reads.
/// </summary>
public sealed class MethodEncryptionMetadata
{
    public const int MagicLength = 16;
    public const int HeaderBytes = 8;
    public const int EntryBytes = 16;
    public const int KeyOffset = 12;

    private static readonly byte[] MagicBytes =
    [
        0xB1, 0x6E, 0x4A, 0xC8, 0x03, 0xF5, 0x9D, 0x2A,
        0x77, 0xE0, 0x14, 0x8C, 0x5B, 0xD3, 0xA6, 0x19
    ];

    /// <summary>
    /// Sentinel prefix of the RVA/key blob. Length is <see cref="MagicLength"/>.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => MagicBytes;

    public IReadOnlyList<EncryptedMethodBody> Entries { get; }

    public MethodEncryptionMetadata(IReadOnlyList<EncryptedMethodBody> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new ArgumentException("At least one method is required.", nameof(entries));
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Method is null || entries[i].Key == 0)
                throw new ArgumentException("Each entry must have a method and a non-zero key.", nameof(entries));
        }

        Entries = entries.ToArray();
    }

    public static int BlobSize(int methodCount) =>
        MagicLength + HeaderBytes + methodCount * EntryBytes;
}
