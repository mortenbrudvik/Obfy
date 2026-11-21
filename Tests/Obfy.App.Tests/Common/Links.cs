using System.ComponentModel;
using System.Diagnostics;

namespace Obfy.App.Tests.Common;

/// <summary>
/// Directory junctions for the tests that need a second spelling of a folder. A junction needs no elevation on Windows
/// (a symbolic link does), so <c>mklink /J</c> is the way to make one from a test.
/// </summary>
internal static class Junctions
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    /// <summary>Why a junction cannot be made here, or null when it can. Probed once.</summary>
    public static string? WhyUnavailable => Unavailable.Value;

    /// <summary>Makes <paramref name="link"/> a junction to the existing folder <paramref name="target"/>.</summary>
    public static bool TryCreate(string link, string target, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "junctions are a Windows feature";
            return false;
        }

        try
        {
            var start = new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0 && Directory.Exists(link))
                return true;

            error = $"mklink /J exited with {process.ExitCode}: {output.Trim()}";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), $"JunctionProbe_{Guid.NewGuid():N}");
        try
        {
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            return TryCreate(Path.Combine(root, "link"), target, out var error) ? null : error;
        }
        finally
        {
            try
            {
                // Remove the junction itself first: a recursive delete must not follow it into the target.
                var link = Path.Combine(root, "link");
                if (Directory.Exists(link))
                    Directory.Delete(link);
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}

/// <summary>A fact that is skipped, with the reason, where a junction cannot be created.</summary>
internal sealed class JunctionFactAttribute : FactAttribute
{
    public JunctionFactAttribute()
    {
        if (Junctions.WhyUnavailable is { } why)
            Skip = $"Cannot create a directory junction here: {why}";
    }
}

/// <summary>
/// File symbolic links for the tests that need a link in a file's own name. Creating one takes elevation or Developer
/// Mode, so the tests that use it are skipped, with the reason, where it is refused.
/// </summary>
internal static class SymbolicLinks
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    /// <summary>Why a file symbolic link cannot be made here, or null when it can. Probed once.</summary>
    public static string? WhyUnavailable => Unavailable.Value;

    /// <summary>Makes <paramref name="link"/> a symbolic link to the file <paramref name="target"/>.</summary>
    public static bool TryCreate(string link, string target, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), $"SymlinkProbe_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, "target.txt");
            File.WriteAllText(target, "x");
            return TryCreate(Path.Combine(root, "link.txt"), target, out var error) ? null : error;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
            }
        }
    }
}

/// <summary>A fact that is skipped, with the reason, where a file symbolic link cannot be created.</summary>
internal sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        if (SymbolicLinks.WhyUnavailable is { } why)
            Skip = $"Cannot create a file symbolic link here: {why}";
    }
}
