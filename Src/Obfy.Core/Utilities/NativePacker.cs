using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Replaces a managed entry-point assembly with the embedded win-x64 native host plus an
/// AES-256-CBC overlay of the obfuscated managed PE (72-byte header + ciphertext; key and IV
/// live in the header). Writes a sibling <c>.runtimeconfig.json</c>. Not a sibling launcher:
/// the managed PE is gone on success. Runtimeconfig TFM prefers
/// <c>TargetFrameworkAttribute</c> / the input sibling JSON, then the Obfy process version
/// (same fallback as <see cref="ManagedLauncherPacker"/>).
/// </summary>
public static class NativePacker
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";

    public static ReadOnlySpan<byte> Magic => "OBP1"u8;
    public static ReadOnlySpan<byte> Sentinel => "OBPYOVL1"u8;

    /// <summary>Overlay header layout matching <c>ObfyOverlayHeader</c> in host.c.</summary>
    internal static class OverlayHeader
    {
        public const int Size = 72;
        public const uint Version = 1;
        public const uint FlagAes = 1;
        public const int MagicOffset = 0;
        public const int VersionOffset = 4;
        public const int HeaderSizeOffset = 8;
        public const int FlagsOffset = 12;
        public const int TfmMajorOffset = 16;
        public const int PayloadLengthOffset = 20;
        public const int IvOffset = 24;
        public const int KeyOffset = 40;
    }

    public static string RuntimeConfigPathFor(string assemblyPath) =>
        Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

    /// <summary>
    /// True when <paramref name="path"/> looks like a stamped native host (MZ + overlay magic).
    /// Used by incremental cache hits so a leftover managed PE plus dummy JSON is not a hit.
    /// </summary>
    public static bool LooksLikePackedHost(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var fs = File.OpenRead(path);
            if (fs.Length < Magic.Length + 2)
                return false;
            Span<byte> mz = stackalloc byte[2];
            if (fs.Read(mz) != 2 || mz[0] != (byte)'M' || mz[1] != (byte)'Z')
                return false;
            fs.Position = 0;
            var buffer = new byte[checked((int)Math.Min(fs.Length, 1024 * 1024))];
            var read = fs.Read(buffer);
            if (buffer.AsSpan(0, read).IndexOf(Magic) >= 0)
                return true;
            if (fs.Length <= buffer.Length)
                return false;
            var rest = new byte[fs.Length - buffer.Length];
            fs.ReadExactly(rest);
            return rest.AsSpan().IndexOf(Magic) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypts the obfuscated PE at <paramref name="assemblyPath"/> into an overlay, stamps the
    /// embedded native host, and replaces the managed PE in place. The AES key is
    /// <see cref="IncrementalCache.ToAes256Key"/> (hex of <see cref="IncrementalCache.ComputeKey"/>,
    /// no second hash). Throws <see cref="InvalidOperationException"/> without a
    /// <c>Packing failed:</c> prefix.
    /// </summary>
    public static string Pack(string assemblyPath, ObfySettings settings, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("Packing requires an existing assembly file.");

        using (var module = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath)))
        {
            if (module.EntryPoint == null)
                throw new InvalidOperationException("Packing requires an assembly with an entry point.");
        }

        var tempPath = assemblyPath + ".obfypack";
        var runtimeConfigPath = RuntimeConfigPathFor(assemblyPath);
        var runtimeConfigTemp = runtimeConfigPath + ".obfytmp";
        try
        {
            var managed = File.ReadAllBytes(assemblyPath);
            ushort subsystem;
            using (var pe = new PEReader(new MemoryStream(managed)))
            {
                var peHeader = pe.PEHeaders.PEHeader
                    ?? throw new InvalidOperationException("Packing requires a valid PE optional header.");
                subsystem = (ushort)peHeader.Subsystem;
            }

            var key = IncrementalCache.ToAes256Key(inputPath, settings);
            var iv = HMACSHA256.HashData(key, "obfy-pack-iv"u8)[..16];
            var ciphertext = EncryptAes(managed, key, iv);
            var tfmMajor = ResolveTfmMajor(managed, inputPath);

            var stub = LoadEmbeddedStub();
            StampStub(stub, subsystem);

            using (var output = File.Create(tempPath))
            {
                output.Write(stub);
                output.Write(BuildHeader(ciphertext, iv, key, tfmMajor));
                output.Write(ciphertext);
            }

            WriteRuntimeConfig(runtimeConfigTemp, tfmMajor);

            if (OperatingSystem.IsWindows())
                File.Replace(tempPath, assemblyPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, assemblyPath, overwrite: true);

            File.Move(runtimeConfigTemp, runtimeConfigPath, overwrite: true);
            return assemblyPath;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            TryDelete(runtimeConfigTemp);
            if (ex is InvalidOperationException)
                throw;
            throw new InvalidOperationException($"{ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    private static byte[] LoadEmbeddedStub()
    {
        using var stream = typeof(NativePacker).Assembly.GetManifestResourceStream(EmbeddedName);
        if (stream is null)
            throw new InvalidOperationException("The native host is missing.");

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static void StampStub(byte[] stub, ushort subsystem)
    {
        var sentinelAt = -1;
        var count = 0;
        for (var i = 0; i <= stub.Length - Sentinel.Length; i++)
        {
            if (stub.AsSpan(i, Sentinel.Length).SequenceEqual(Sentinel))
            {
                sentinelAt = i;
                count++;
            }
        }

        if (count != 1)
            throw new InvalidOperationException("The native host is corrupt.");

        BinaryPrimitives.WriteUInt64LittleEndian(stub.AsSpan(sentinelAt), (ulong)stub.Length);

        int subsystemOffset;
        using (var pe = new PEReader(new MemoryStream(stub)))
        {
            // IMAGE_OPTIONAL_HEADER.Subsystem is at offset 68 for both PE32 and PE32+.
            subsystemOffset = pe.PEHeaders.PEHeaderStartOffset + 68;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(stub.AsSpan(subsystemOffset), subsystem);
    }

    // Ciphertext does not prepend the IV (EncryptionHelper.EncryptBytesAes does).
    private static byte[] EncryptAes(byte[] plaintext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    internal static byte[] BuildHeader(byte[] ciphertext, byte[] iv, byte[] key, uint tfmMajor)
    {
        var header = new byte[OverlayHeader.Size];
        Magic.CopyTo(header.AsSpan(OverlayHeader.MagicOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OverlayHeader.VersionOffset), OverlayHeader.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OverlayHeader.HeaderSizeOffset), (uint)OverlayHeader.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OverlayHeader.FlagsOffset), OverlayHeader.FlagAes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OverlayHeader.TfmMajorOffset), tfmMajor);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OverlayHeader.PayloadLengthOffset), (uint)ciphertext.Length);
        iv.CopyTo(header.AsSpan(OverlayHeader.IvOffset));
        key.CopyTo(header.AsSpan(OverlayHeader.KeyOffset));
        return header;
    }

    private static void WriteRuntimeConfig(string runtimeConfigPath, uint major)
    {
        var runtimeConfig = $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{major}}.0",
                "rollForward": "LatestMinor",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{major}}.0.0"
                }
              }
            }
            """;
        File.WriteAllText(runtimeConfigPath, runtimeConfig);
    }

    internal static uint ResolveTfmMajor(byte[] managed, string inputPath)
    {
        if (TryTfmFromAttribute(managed, out var major))
            return major;
        if (TryTfmFromRuntimeConfig(Path.ChangeExtension(inputPath, ".runtimeconfig.json"), out major))
            return major;
        return (uint)Environment.Version.Major;
    }

    private static bool TryTfmFromAttribute(byte[] managed, out uint major)
    {
        major = 0;
        try
        {
            using var module = ModuleDefMD.Load(managed);
            var attr = module.Assembly?.CustomAttributes.FirstOrDefault(static a =>
                a.TypeFullName is "System.Runtime.Versioning.TargetFrameworkAttribute");
            if (attr is null || attr.ConstructorArguments.Count == 0)
                return false;
            var value = attr.ConstructorArguments[0].Value?.ToString();
            if (string.IsNullOrEmpty(value))
                return false;
            var vIndex = value.LastIndexOf('v');
            if (vIndex < 0 || vIndex + 1 >= value.Length || !char.IsDigit(value[vIndex + 1]))
                return false;
            var majorEnd = vIndex + 1;
            while (majorEnd < value.Length && char.IsDigit(value[majorEnd]))
                majorEnd++;
            return uint.TryParse(value.AsSpan(vIndex + 1, majorEnd - vIndex - 1), out major) && major > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryTfmFromRuntimeConfig(string path, out uint major)
    {
        major = 0;
        if (!File.Exists(path))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("runtimeOptions", out var opts) ||
                !opts.TryGetProperty("tfm", out var tfmEl))
                return false;
            var tfm = tfmEl.GetString();
            if (tfm is null || !tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                return false;
            var rest = tfm[3..];
            var dot = rest.IndexOf('.');
            var majorText = dot < 0 ? rest : rest[..dot];
            return uint.TryParse(majorText, out major) && major > 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"NativePacker: failed to delete '{path}': {ex.Message}");
        }
    }
}
