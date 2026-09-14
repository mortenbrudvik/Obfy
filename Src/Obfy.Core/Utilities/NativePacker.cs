using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Replaces a managed entry-point assembly with the embedded win-x64 native host plus an
/// AES-256-CBC overlay of the original PE. Writes a sibling <c>.runtimeconfig.json</c>.
/// Not a sibling launcher: the managed PE is gone on success.
/// </summary>
public static class NativePacker
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";
    public const int HeaderSize = 72;
    public static readonly byte[] Magic = "OBP1"u8.ToArray();
    public static readonly byte[] Sentinel = "OBPYOVL1"u8.ToArray();

    public static string RuntimeConfigPathFor(string assemblyPath) =>
        Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

    /// <summary>
    /// Encrypts <paramref name="assemblyPath"/> into an overlay, stamps the embedded native host,
    /// and replaces the managed PE in place. The AES key is
    /// <see cref="IncrementalCache.ComputeKey"/> hex-decoded (no second hash). Throws
    /// <see cref="InvalidOperationException"/> without a <c>Packing failed:</c> prefix.
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
        try
        {
            var managed = File.ReadAllBytes(assemblyPath);
            ushort subsystem;
            using (var pe = new PEReader(new MemoryStream(managed)))
            {
                subsystem = (ushort)pe.PEHeaders.PEHeader!.Subsystem;
            }

            var key = Convert.FromHexString(IncrementalCache.ComputeKey(inputPath, settings));
            var iv = HMACSHA256.HashData(key, "obfy-pack-iv"u8)[..16];
            var ciphertext = EncryptAes(managed, key, iv);

            var stub = LoadEmbeddedStub();
            StampStub(stub, subsystem);

            using (var output = File.Create(tempPath))
            {
                output.Write(stub);
                output.Write(BuildHeader(ciphertext, iv, key));
                output.Write(ciphertext);
            }

            WriteRuntimeConfig(runtimeConfigPath);

            if (OperatingSystem.IsWindows())
                File.Replace(tempPath, assemblyPath, destinationBackupFileName: null);
            else
            {
                File.Delete(assemblyPath);
                File.Move(tempPath, assemblyPath);
            }

            return assemblyPath;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            TryDelete(runtimeConfigPath);
            if (ex is InvalidOperationException)
                throw;
            throw new InvalidOperationException(ex.Message, ex);
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

    private static byte[] BuildHeader(byte[] ciphertext, byte[] iv, byte[] key)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header.AsSpan());
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)Environment.Version.Major);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)ciphertext.Length);
        iv.CopyTo(header.AsSpan(24));
        key.CopyTo(header.AsSpan(40));
        return header;
    }

    private static void WriteRuntimeConfig(string runtimeConfigPath)
    {
        var major = Environment.Version.Major;
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
