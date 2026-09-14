using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Per-build VM opcode permutation and XOR key derived from the incremental-cache seed.
/// </summary>
public static class VmSeed
{
    private const int OpCount = 77;
    private static readonly byte[] OpMapLabel = Encoding.UTF8.GetBytes("opmap");
    private static readonly byte[] XorLabel = Encoding.UTF8.GetBytes("xor");

    public static byte[] Compute(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrEmpty(context.InputPath) && File.Exists(context.InputPath))
            return Convert.FromHexString(IncrementalCache.ComputeKey(context.InputPath, context.Settings));

        var module = context.RequireModule();
        using var ms = new MemoryStream();
        module.Write(ms);
        var input = ms.ToArray();
        var json = JsonSerializer.Serialize(context.Settings, IncrementalCache.JsonOptions);
        var version = typeof(IncrementalCache).Assembly.GetName().Version?.ToString() ?? "0";
        var stamp = Encoding.UTF8.GetBytes("obfy-cache|" + version + "|");
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var payload = new byte[stamp.Length + input.Length + jsonBytes.Length];
        Buffer.BlockCopy(stamp, 0, payload, 0, stamp.Length);
        Buffer.BlockCopy(input, 0, payload, stamp.Length, input.Length);
        Buffer.BlockCopy(jsonBytes, 0, payload, stamp.Length + input.Length, jsonBytes.Length);
        return SHA256.HashData(payload);
    }

    public static byte[] CreateOpMap(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var shuffled = new byte[OpCount];
        for (var i = 0; i < OpCount; i++)
            shuffled[i] = (byte)(i + 1);

        using var hmac = new HMACSHA256(seed);
        var stream = new HmacByteStream(hmac, OpMapLabel);
        for (var i = OpCount - 1; i >= 1; i--)
        {
            var j = stream.NextByte() % (i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var map = new byte[256];
        Array.Fill(map, (byte)0xFF);
        for (var i = 0; i < OpCount; i++)
            map[shuffled[i]] = (byte)(i + 1);
        return map;
    }

    public static byte[] CreateXorKey(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var hash = HMACSHA256.HashData(seed, XorLabel);
        var key = new byte[8];
        Buffer.BlockCopy(hash, 0, key, 0, 8);
        return key;
    }

    public static void Apply(byte[] blob, byte[] opMap, byte[] xorKey)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(opMap);
        ArgumentNullException.ThrowIfNull(xorKey);
        if (opMap.Length != 256)
            throw new ArgumentException("opMap must be 256 bytes.", nameof(opMap));
        if (xorKey.Length != 8)
            throw new ArgumentException("xorKey must be 8 bytes.", nameof(xorKey));

        var inverse = new byte[256];
        Array.Fill(inverse, (byte)0xFF);
        for (var encoded = 0; encoded < 256; encoded++)
        {
            var internalOp = opMap[encoded];
            if (internalOp != 0xFF)
                inverse[internalOp] = (byte)encoded;
        }

        for (var i = 0; i < blob.Length;)
        {
            var size = VmIsa.EncodedSize(blob, i);
            blob[i] = inverse[blob[i]];
            i += size;
        }

        for (var i = 0; i < blob.Length; i++)
            blob[i] ^= xorKey[i % 8];
    }

    private sealed class HmacByteStream
    {
        private readonly HMACSHA256 _hmac;
        private readonly byte[] _label;
        private byte[] _block = Array.Empty<byte>();
        private int _index;
        private uint _counter;

        public HmacByteStream(HMACSHA256 hmac, byte[] label)
        {
            _hmac = hmac;
            _label = label;
        }

        public byte NextByte()
        {
            if (_index >= _block.Length)
            {
                var msg = new byte[_label.Length + 4];
                Buffer.BlockCopy(_label, 0, msg, 0, _label.Length);
                BinaryPrimitives.WriteUInt32BigEndian(msg.AsSpan(_label.Length), _counter);
                _block = _hmac.ComputeHash(msg);
                _index = 0;
                _counter++;
            }

            return _block[_index++];
        }
    }
}
