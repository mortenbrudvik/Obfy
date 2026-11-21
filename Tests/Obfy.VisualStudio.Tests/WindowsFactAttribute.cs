namespace Obfy.VisualStudio.Tests;

/// <summary>
/// The CLI runner is exercised against real child processes (<c>cmd.exe</c>, a copy of <c>ping.exe</c>),
/// so these tests only run on Windows, where the extension itself runs.
/// </summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Needs cmd.exe and the Windows process tree.";
    }
}
