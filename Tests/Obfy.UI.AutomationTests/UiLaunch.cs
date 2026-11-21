using System.Diagnostics;
using System.IO;
using FlaUI.Core;

namespace Obfy.UI.AutomationTests;

/// <summary>
/// Launches Obfy with a private preferences file and a private WebView2 profile, so tests touch neither %APPDATA%\Obfy nor
/// the real WebView2 profile of an Obfy the developer may have open. Both are set on the launched process only.
/// </summary>
internal static class UiLaunch
{
    /// <summary>The preferences override the app reads (UiPreferences).</summary>
    public const string PreferencesVariable = "OBFY_UI_PREFERENCES";

    /// <summary>The profile override ShellViewModel reads, so a test's WebView2 never shares a profile with a real Obfy.</summary>
    public const string WebViewProfileVariable = "OBFY_WEBVIEW2_USER_DATA";

    /// <summary>WebView2 reads this itself when it starts its browser; no host code passes it on.</summary>
    public const string BrowserArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    /// <summary>
    /// Starts the app under FlaUI. The window-level smoke test uses this; the page is not driven. The preferences and the
    /// WebView2 profile are private to this launch (a fresh folder under the temp folder), set on the child's start info:
    /// nothing is written to this process's environment, so other tests and launches are unaffected.
    /// </summary>
    public static Application Launch(string path, string? arguments = null)
    {
        var work = Path.Combine(Path.GetTempPath(), "obfy-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        if (!string.IsNullOrEmpty(arguments))
            info.Arguments = arguments;

        info.Environment[PreferencesVariable] = Path.Combine(work, "ui-preferences.json");
        info.Environment[WebViewProfileVariable] = Path.Combine(work, "webview2");

        return Application.Launch(info);
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
