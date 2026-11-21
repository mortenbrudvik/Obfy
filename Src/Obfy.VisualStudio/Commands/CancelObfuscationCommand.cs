using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace Obfy.VisualStudio.Commands;

/// <summary>
/// Command that stops the obfuscation runs that are in flight (manual and post-build). Shown only while one runs.
/// </summary>
[Command(PackageGuids.CommandSetString, PackageIds.cmdidCancelObfuscation)]
internal sealed class CancelObfuscationCommand : BaseCommand<CancelObfuscationCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var cancelled = ObfyPackage.Runs.CancelAll();
        if (cancelled > 0)
        {
            ObfyPackage.OutputService?.Info($"Cancelling {cancelled} obfuscation run(s)...");
            await VS.StatusBar.ShowMessageAsync("Cancelling obfuscation...");
        }
        else
        {
            await VS.StatusBar.ShowMessageAsync("No obfuscation is running");
        }
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        // The .vsct declares the button DefaultInvisible + DynamicVisibility: it is in the menu only during a run.
        var running = ObfyPackage.Runs.ActiveRunCount > 0;
        Command.Visible = running;
        Command.Enabled = running;
    }
}
