using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Remembers which output assembly the post-build step already obfuscated in place, so a build that did not
/// produce a new assembly (an up-to-date project, or a Debug build next to an obfuscated Release output) does
/// not obfuscate the same file a second time.
/// </summary>
/// <remarks>
/// The stamp is the size and last-write time of the assembly right after it was obfuscated. A real build
/// replaces the file (MSBuild copies <c>obj\</c> over <c>bin\</c> because the obfuscated file differs from the
/// compiler output), which changes at least one of the two. Stamps live under the project's <c>obj\</c>
/// folder, never next to the assembly, so they are not shipped with the build output. The stamp path and
/// content match the Rider plugin's, so the two IDEs agree on what has been obfuscated.
/// </remarks>
public static class PostBuildStamp
{
    private const string StampFolder = "obfy-postbuild";

    public static bool IsAlreadyObfuscated(string projectDir, string assemblyPath)
    {
        try
        {
            var fingerprint = Fingerprint(assemblyPath);
            if (fingerprint is null)
                return false;

            var stampPath = StampPath(projectDir, assemblyPath);
            return File.Exists(stampPath) && string.Equals(File.ReadAllText(stampPath), fingerprint, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable stamp means "unknown": obfuscate rather than leave the output unprotected.
            return false;
        }
    }

    public static void Record(string projectDir, string assemblyPath)
    {
        try
        {
            var fingerprint = Fingerprint(assemblyPath);
            if (fingerprint is null)
                return;

            var stampPath = StampPath(projectDir, assemblyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            File.WriteAllText(stampPath, fingerprint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a stamp the next build obfuscates again; that must not turn a successful run into a failure.
        }
    }

    private static string? Fingerprint(string assemblyPath)
    {
        var file = new FileInfo(assemblyPath);
        if (!file.Exists)
            return null;

        // Milliseconds since the Unix epoch, the resolution the Rider plugin's stamp uses, so both IDEs agree.
        var lastWrite = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1}", file.Length, lastWrite);
    }

    private static string StampPath(string projectDir, string assemblyPath)
    {
        var key = Path.GetFullPath(assemblyPath).ToUpperInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var name = new StringBuilder(Path.GetFileNameWithoutExtension(assemblyPath)).Append('-');
        for (var i = 0; i < 8; i++)
            name.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

        return Path.Combine(projectDir, "obj", StampFolder, name + ".stamp");
    }
}
