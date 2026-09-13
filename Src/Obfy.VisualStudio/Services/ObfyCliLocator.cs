using System;
using System.Collections.Generic;
using System.IO;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Resolves <c>obfy.exe</c> from install directories and PATH (no VS hive required).
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

            var exePath = Path.Combine(dir, ExeName);
            if (File.Exists(exePath))
                return exePath;
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
            yield return dir;

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools");
    }
}
