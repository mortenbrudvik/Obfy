using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Strong-name signing for obfuscated PEs. The first <c>Module.Write</c> must set
/// <c>ModuleWriterOptions.StrongNameKey</c> so the signature directory is allocated.
/// After PE patches (method-IL XOR, anti-tamper hash), <see cref="SignInPlace"/> refreshes
/// the signature blob without rewriting the image.
/// </summary>
internal static class AssemblySigner
{
    /// <summary>
    /// Loads the strong-name key when signing is enabled. Returns null when signing is off.
    /// Throws if signing is on but the key cannot be loaded.
    /// </summary>
    public static StrongNameKey? TryLoadKey(SigningSettings settings)
    {
        if (!settings.Enabled)
            return null;
        return LoadKey(settings);
    }

    public static StrongNameKey LoadKey(SigningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.KeyFile))
            throw new InvalidOperationException("Signing is enabled but no key file was specified.");

        if (!File.Exists(settings.KeyFile))
            throw new InvalidOperationException($"Signing key file not found: {settings.KeyFile}");

        var path = settings.KeyFile;
        if (IsPkcs12(path))
        {
            var password = ReadPassword(settings);
            try
            {
                using var cert = X509CertificateLoader.LoadPkcs12FromFile(
                    path, password, X509KeyStorageFlags.Exportable);
                using var rsa = cert.GetRSAPrivateKey()
                    ?? throw new InvalidOperationException("PFX file does not contain an RSA private key.");
#pragma warning disable SYSLIB0028, CA1416
                using var csp = new RSACryptoServiceProvider();
                csp.ImportParameters(rsa.ExportParameters(includePrivateParameters: true));
                var blob = csp.ExportCspBlob(includePrivateParameters: true);
#pragma warning restore SYSLIB0028, CA1416
                // PFX keys are typically CALG_RSA_KEYX; strong-name requires CALG_RSA_SIGN (0x00002400).
                if (blob.Length >= 8)
                {
                    blob[4] = 0x00;
                    blob[5] = 0x24;
                    blob[6] = 0x00;
                    blob[7] = 0x00;
                }

                return new StrongNameKey(blob);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidKeyException)
            {
                throw new InvalidOperationException(
                    $"Failed to load PFX key '{path}'. Ensure the file contains an RSA private key and that " +
                    $"environment variable '{settings.PasswordEnvironmentVariable}' holds the password. {ex.Message}",
                    ex);
            }
        }

        try
        {
            return new StrongNameKey(path);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException or BadImageFormatException or InvalidKeyException)
        {
            throw new InvalidOperationException(
                $"Failed to load strong-name key '{path}'. Ensure the file is an .snk with a private key. {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Overwrites the strong-name signature blob of an already-written PE. Does not reload or rewrite IL.
    /// </summary>
    public static void SignInPlace(string assemblyPath, StrongNameKey key)
    {
        try
        {
            var offset = ReadStrongNameSignatureOffset(assemblyPath);
            using (var fs = File.Open(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                new StrongNameSigner(fs).WriteSignature(key, offset);
            }

            using var verify = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath));
            if (!verify.IsStrongNameSigned)
            {
                throw new InvalidOperationException(
                    $"Signing did not produce a strong-name signature for '{assemblyPath}'. " +
                    "Check that the key file contains a private key.");
            }
        }
        catch (Exception ex) when (
            ex is CryptographicException or ArgumentException or BadImageFormatException or IOException)
        {
            throw new InvalidOperationException(
                $"Failed to strong-name '{assemblyPath}'. {ex.Message}", ex);
        }
    }

    internal static bool IsPkcs12(string path) =>
        path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".p12", StringComparison.OrdinalIgnoreCase);

    private static long ReadStrongNameSignatureOffset(string assemblyPath)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath));
        var dir = module.Metadata.ImageCor20Header.StrongNameSignature;
        if (dir.VirtualAddress == 0)
        {
            throw new InvalidOperationException(
                $"Assembly '{assemblyPath}' has no strong-name signature directory. " +
                "The module must be written with ModuleWriterOptions.StrongNameKey set.");
        }

        return (long)(uint)module.Metadata.PEImage.ToFileOffset(dir.VirtualAddress);
    }

    private static string ReadPassword(SigningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PasswordEnvironmentVariable))
        {
            throw new InvalidOperationException(
                "PFX signing requires Signing.PasswordEnvironmentVariable to name an environment variable that holds the password.");
        }

        var value = Environment.GetEnvironmentVariable(settings.PasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"PFX password environment variable '{settings.PasswordEnvironmentVariable}' is not set or empty.");
        }

        return value;
    }
}
