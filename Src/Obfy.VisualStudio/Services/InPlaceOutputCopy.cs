using System;
using System.IO;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Copies CLI temp output (primary assembly plus packing sidecars) onto an in-place destination.
/// Stages next to the destination so <see cref="File.Replace"/> stays on the same volume.
/// </summary>
public static class InPlaceOutputCopy
{
    public static void CopyTempDirectoryToDestination(string tempOutputPath, string destinationPath)
    {
        if (string.IsNullOrEmpty(tempOutputPath) || !File.Exists(tempOutputPath))
        {
            throw new FileNotFoundException(
                "CLI succeeded but the temp output was not found; original assembly was not modified.",
                tempOutputPath);
        }

        var destDir = Path.GetDirectoryName(destinationPath);
        var tempDir = Path.GetDirectoryName(tempOutputPath);
        if (string.IsNullOrEmpty(destDir) || string.IsNullOrEmpty(tempDir))
        {
            throw new InvalidOperationException("Temp output and destination paths must include a directory.");
        }

        var staging = destinationPath + ".obfynew";
        File.Copy(tempOutputPath, staging, overwrite: true);
        File.Replace(staging, destinationPath, destinationBackupFileName: null);

        var primaryName = Path.GetFileName(tempOutputPath);
        foreach (var file in Directory.GetFiles(tempDir))
        {
            if (string.Equals(Path.GetFileName(file), primaryName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.EndsWith(".obfynew", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
    }
}
