using System;
using System.IO;

namespace Obfy.VisualStudio.Services;

public static class ProjectSupport
{
    public static bool IsSupportedProjectPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        var ext = Path.GetExtension(fullPath);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase);
    }
}
