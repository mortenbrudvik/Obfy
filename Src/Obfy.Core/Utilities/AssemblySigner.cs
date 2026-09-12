using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Re-signs an obfuscated PE with a strong-name key. Must run after PE patches.
/// </summary>
internal static class AssemblySigner
{
    public static void Sign(string assemblyPath, SigningSettings settings)
    {
        if (!settings.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(settings.KeyFile))
            throw new InvalidOperationException("Signing is enabled but no key file was specified.");

        if (!File.Exists(settings.KeyFile))
            throw new InvalidOperationException($"Signing key file not found: {settings.KeyFile}");

        var key = LoadKey(settings);
        var bytes = File.ReadAllBytes(assemblyPath);
        using var module = ModuleDefMD.Load(bytes);
        var options = new ModuleWriterOptions(module)
        {
            StrongNameKey = key
        };
        module.Write(assemblyPath, options);
    }

    private static StrongNameKey LoadKey(SigningSettings settings)
    {
        var path = settings.KeyFile!;
        if (path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
        {
            var password = ReadPassword(settings);
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "PFX signing requires Signing.PasswordEnvironmentVariable to name an environment variable that holds the password.");

            using var cert = X509CertificateLoader.LoadPkcs12FromFile(
                path, password, X509KeyStorageFlags.Exportable);
            using var rsa = cert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("PFX file does not contain an RSA private key.");
            using var csp = new RSACryptoServiceProvider();
            csp.ImportParameters(rsa.ExportParameters(includePrivateParameters: true));
            return new StrongNameKey(csp.ExportCspBlob(includePrivateParameters: true));
        }

        return new StrongNameKey(path);
    }

    private static string? ReadPassword(SigningSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PasswordEnvironmentVariable))
            return null;
        return Environment.GetEnvironmentVariable(settings.PasswordEnvironmentVariable);
    }
}
