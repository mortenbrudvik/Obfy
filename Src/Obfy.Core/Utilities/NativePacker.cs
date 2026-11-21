using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Replaces a managed entry-point assembly with the embedded win-x64 native host plus an
/// AES-256-CBC overlay of the obfuscated managed PE (72-byte header + ciphertext; key and IV
/// live in the header). Writes a sibling <c>.runtimeconfig.json</c>. Not a sibling launcher:
/// the managed PE is gone on success. Runtimeconfig TFM prefers
/// <c>TargetFrameworkAttribute</c> / the input sibling JSON, then the framework references of the
/// payload. The payload must target .NET 8 or newer; anything else is refused before any file
/// changes (use <c>packing.rid: portable</c>).
/// </summary>
public static class NativePacker
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";

    /// <summary>
    /// Lowest .NET (Microsoft.NETCore.App) major the packed host can run: the bootstrap it loads
    /// into the payload's runtime (<c>Obfy.PackedBootstrap</c>) targets net8.0.
    /// </summary>
    internal const uint MinPayloadMajor = 8;

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
    /// embedded native host, and replaces the managed PE in place. The AES key is random for
    /// this pack and is not the <c>.obfycache</c> digest. Throws
    /// <see cref="InvalidOperationException"/> without a <c>Packing failed:</c> prefix.
    /// The swap is transactional: if the runtimeconfig cannot be published the original PE is
    /// restored, and a payload the host cannot run is refused before any file is touched.
    /// </summary>
    public static string Pack(string assemblyPath, ObfySettings settings, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("Packing requires an existing assembly file.");

        var managed = File.ReadAllBytes(assemblyPath);
        using (var module = ModuleDefMD.Load(managed))
        {
            if (module.EntryPoint == null)
                throw new InvalidOperationException("Packing requires an assembly with an entry point.");
        }

        // Decide whether the payload can run under the packed host before anything on disk changes.
        var target = ResolvePayloadTarget(managed, inputPath);
        ValidatePayloadTarget(target);
        var tfmMajor = target.Major;

        var tempPath = assemblyPath + ".obfypack";
        var runtimeConfigPath = RuntimeConfigPathFor(assemblyPath);
        var runtimeConfigTemp = runtimeConfigPath + ".obfytmp";
        try
        {
            ushort subsystem;
            using (var pe = new PEReader(new MemoryStream(managed)))
            {
                var peHeader = pe.PEHeaders.PEHeader
                    ?? throw new InvalidOperationException("Packing requires a valid PE optional header.");
                subsystem = (ushort)peHeader.Subsystem;
            }

            var key = RandomNumberGenerator.GetBytes(32);
            var iv = HMACSHA256.HashData(key, "obfy-pack-iv"u8)[..16];
            var ciphertext = EncryptAes(managed, key, iv);

            var stub = LoadEmbeddedStub();
            StampStub(stub, subsystem);

            using (var output = File.Create(tempPath))
            {
                output.Write(stub);
                output.Write(BuildHeader(ciphertext, iv, key, tfmMajor));
                output.Write(ciphertext);
            }

            File.WriteAllText(runtimeConfigTemp, BuildRuntimeConfig(tfmMajor, inputPath));

            Publish(tempPath, assemblyPath, runtimeConfigTemp, runtimeConfigPath);
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

    /// <summary>
    /// Swaps the packed host in for the managed PE and then publishes the runtimeconfig. The runtimeconfig
    /// move is the only step after the swap and replaces its target atomically, so when it fails the
    /// original PE is put back from its backup: the output is never an unrunnable native stub with no
    /// (or a stale) configuration.
    /// </summary>
    private static void Publish(string packedTemp, string assemblyPath, string runtimeConfigTemp, string runtimeConfigPath)
    {
        var peBackup = assemblyPath + ".obfybak";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                File.Replace(packedTemp, assemblyPath, peBackup);
            }
            else
            {
                File.Copy(assemblyPath, peBackup, overwrite: true);
                File.Move(packedTemp, assemblyPath, overwrite: true);
            }

            File.Move(runtimeConfigTemp, runtimeConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            var restored = TryRestore(peBackup, assemblyPath);
            TryDelete(peBackup);
            throw new InvalidOperationException(
                restored
                    ? $"{ex.GetType().Name}: {ex.Message} The original assembly was restored."
                    : $"{ex.GetType().Name}: {ex.Message} Restoring the original assembly failed; the output may be an unusable packed host.",
                ex);
        }

        TryDelete(peBackup);
    }

    private static bool TryRestore(string backupPath, string destination)
    {
        try
        {
            if (!File.Exists(backupPath))
                return true; // the swap itself failed, so nothing was replaced
            File.Move(backupPath, destination, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"NativePacker: failed to restore '{destination}': {ex.Message}");
            return false;
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

    /// <summary>
    /// Runtimeconfig for a packed payload. When the input has a sibling <c>.runtimeconfig.json</c>
    /// that names its frameworks, its <c>runtimeOptions</c> (frameworks such as
    /// <c>Microsoft.WindowsDesktop.App</c> / <c>Microsoft.AspNetCore.App</c>, <c>configProperties</c>,
    /// roll-forward policy) are kept, with any <c>Microsoft.*</c> framework raised to at least
    /// <paramref name="major"/>. Otherwise a minimal <c>Microsoft.NETCore.App</c> config is generated.
    /// </summary>
    internal static string BuildRuntimeConfig(uint major, string inputPath)
    {
        var original = TryReadRuntimeOptions(Path.ChangeExtension(inputPath, ".runtimeconfig.json"));
        if (original is not null)
        {
            original["tfm"] = $"net{major}.0";
            if (original["framework"] is JsonObject single)
                RaiseFramework(single, major);
            if (original["frameworks"] is JsonArray many)
            {
                foreach (var node in many)
                {
                    if (node is JsonObject framework)
                        RaiseFramework(framework, major);
                }
            }

            return new JsonObject { ["runtimeOptions"] = original }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        return $$"""
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
    }

    private static void RaiseFramework(JsonObject framework, uint major)
    {
        var name = framework["name"]?.GetValue<string>();
        var version = framework["version"]?.GetValue<string>();
        if (name is null || !name.StartsWith("Microsoft.", StringComparison.Ordinal) || version is null)
            return;
        if (Version.TryParse(version, out var parsed) && parsed.Major < major)
            framework["version"] = $"{major}.0.0";
    }

    private static JsonObject? TryReadRuntimeOptions(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["runtimeOptions"] is not JsonObject options)
                return null;
            // A self-contained config has neither; fall back to the generated shared-framework config.
            if (options["framework"] is not JsonObject && options["frameworks"] is not JsonArray)
                return null;
            return (JsonObject)options.DeepClone();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    internal readonly record struct PayloadTarget(string Framework, uint Major, string? FullVersion = null)
    {
        public bool IsNetCoreApp => string.Equals(Framework, ".NETCoreApp", StringComparison.OrdinalIgnoreCase);

        public override string ToString() => $"{Framework} {FullVersion ?? $"{Major}.0"}";
    }

    internal static uint ResolveTfmMajor(byte[] managed, string inputPath) =>
        ResolvePayloadTarget(managed, inputPath).Major;

    internal static PayloadTarget ResolvePayloadTarget(byte[] managed, string inputPath)
    {
        if (TryTargetFromAttribute(managed, out var target))
            return target;
        if (TryTfmFromRuntimeConfig(Path.ChangeExtension(inputPath, ".runtimeconfig.json"), out var major))
            return new PayloadTarget(".NETCoreApp", major);
        if (TryTfmFromFrameworkReference(managed, out major))
            return new PayloadTarget(".NETCoreApp", major);
        throw new InvalidOperationException(
            "Cannot determine the payload target framework. Packing will not write a runtimeconfig from the Obfy host version.");
    }

    /// <summary>
    /// Fails, before the managed PE is touched, when the packed host cannot run the payload:
    /// .NET Framework / .NET Standard assemblies, and .NET Core apps older than the net8.0 bootstrap.
    /// </summary>
    internal static void ValidatePayloadTarget(PayloadTarget target)
    {
        if (target.IsNetCoreApp && target.Major >= MinPayloadMajor)
            return;

        throw new InvalidOperationException(
            $"Native packing cannot run this assembly: it targets {target}, but the packed host needs " +
            $".NET {MinPayloadMajor}.0 or newer (Microsoft.NETCore.App). " +
            "Set packing.rid to 'portable' to use the managed launcher, or retarget the assembly.");
    }

    private static bool TryTfmFromFrameworkReference(byte[] managed, out uint major)
    {
        major = 0;
        try
        {
            using var module = ModuleDefMD.Load(managed);
            uint runtime = 0;
            uint coreLib = 0;
            foreach (var reference in module.GetAssemblyRefs())
            {
                var name = reference.Name.String;
                var referenceMajor = (uint)reference.Version.Major;
                if (referenceMajor == 0)
                    continue;
                if (name == "System.Runtime")
                    runtime = referenceMajor;
                else if (name == "System.Private.CoreLib")
                    coreLib = referenceMajor;
            }

            major = runtime != 0 ? runtime : coreLib;
            return major > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryTargetFromAttribute(byte[] managed, out PayloadTarget target)
    {
        target = default;
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
            var name = new FrameworkName(value);
            if (name.Version.Major <= 0)
                return false;
            target = new PayloadTarget(name.Identifier, (uint)name.Version.Major, name.Version.ToString());
            return true;
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
            // .NET 5+ monikers always carry a dot (net8.0); "net48" is .NET Framework, not a major 48.
            var rest = tfm[3..];
            var dot = rest.IndexOf('.');
            return dot > 0 && uint.TryParse(rest[..dot], out major) && major > 0;
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
