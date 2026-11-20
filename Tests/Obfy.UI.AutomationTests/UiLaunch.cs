using System.IO;
using FlaUI.Core;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Launches Obfy with a private preferences file so tests do not touch %APPDATA%\Obfy.
/// </summary>
internal static class UiLaunch
{
    private static readonly object Gate = new();

    public static Application Launch(string path, string? arguments = null)
    {
        lock (Gate)
        {
            var prefs = Path.Combine(Path.GetTempPath(), "obfy-ui-prefs-" + Guid.NewGuid().ToString("N") + ".json");
            Environment.SetEnvironmentVariable("OBFY_UI_PREFERENCES", prefs);
            return string.IsNullOrEmpty(arguments)
                ? Application.Launch(path)
                : Application.Launch(path, arguments);
        }
    }
}
