using dnlib.DotNet;

namespace Obfy.Core.Models;

/// <summary>
/// Method-body encryption state: methods to XOR in the PE after RVAs are assigned,
/// plus the magic blob the runtime decryptor reads.
/// </summary>
public sealed class MethodEncryptionMetadata
{
    public static readonly byte[] Magic =
    [
        0xB1, 0x6E, 0x4A, 0xC8, 0x03, 0xF5, 0x9D, 0x2A,
        0x77, 0xE0, 0x14, 0x8C, 0x5B, 0xD3, 0xA6, 0x19
    ];

    public required IReadOnlyList<MethodDef> Methods { get; init; }

    /// <summary>
    /// Per-method XOR keys, parallel to <see cref="Methods"/>. Never zero.
    /// </summary>
    public required IReadOnlyList<byte> Keys { get; init; }
}
