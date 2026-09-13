using System;
using System.Collections.Generic;
using System.IO;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Resolves <c>obfy.exe</c>. <see cref="Find"/> walks the given directories;
/// <see cref="DefaultSearchDirectories"/> is Program Files, the extension base,
/// PATH (trimmed), then <c>~/.dotnet/tools</c> last.
/// </summary>
public static class ObfyCliLocator
{
    public const string ExeName = "obfy.exe";

    public static string? Find(IEnumerable<string> directories)
    {
        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            try
            {
                var exePath = Path.Combine(dir.Trim(), ExeName);
                if (File.Exists(exePath))
                    return exePath;
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
        }

        return null;
    }

    public static IEnumerable<string> DefaultSearchDirectories()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Obfy");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Obfy");
        yield return AppDomain.CurrentDomain.BaseDirectory;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools");
    }
}
