using System.Diagnostics;
using System.IO;
using FlaUI.Core;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Launches Obfy with a private preferences file so tests do not touch %APPDATA%\Obfy.
/// </summary>
internal static class UiLaunch
{
    /// <summary>The preferences override the app reads (UiPreferences).</summary>
    public const string PreferencesVariable = "OBFY_UI_PREFERENCES";

    /// <summary>The profile override ShellViewModel reads, so a test's WebView2 never shares a profile with a real Obfy.</summary>
    public const string WebViewProfileVariable = "OBFY_WEBVIEW2_USER_DATA";

    /// <summary>WebView2 reads this itself when it starts its browser; no host code passes it on.</summary>
    public const string BrowserArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    private static readonly object Gate = new();

    /// <summary>Starts the app under FlaUI. The window-level smoke test uses this; the page is not driven.</summary>
    public static Application Launch(string path, string? arguments = null)
    {
        lock (Gate)
        {
            var prefs = Path.Combine(Path.GetTempPath(), "obfy-ui-prefs-" + Guid.NewGuid().ToString("N") + ".json");
            Environment.SetEnvironmentVariable(PreferencesVariable, prefs);
            return string.IsNullOrEmpty(arguments)
                ? Application.Launch(path)
                : Application.Launch(path, arguments);
        }
    }

    /// <summary>
    /// Starts the app with WebView2's remote-debugging port open on <paramref name="debugPort"/>, a private profile and
    /// private preferences, both under <paramref name="workDirectory"/>. The environment is set on the child only.
    /// </summary>
    public static Process StartWithDebugPort(string path, IReadOnlyList<string> arguments, int debugPort, string workDirectory)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        info.Environment[PreferencesVariable] = Path.Combine(workDirectory, "ui-preferences.json");
        info.Environment[WebViewProfileVariable] = Path.Combine(workDirectory, "webview2");
        info.Environment[BrowserArgumentsVariable] = $"--remote-debugging-port={debugPort} --remote-allow-origins=*";

        return Process.Start(info) ?? throw new InvalidOperationException("ObfyUI.exe did not start.");
    }
}
