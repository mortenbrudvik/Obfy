using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Obfy.VisualStudio.Commands;
using Obfy.VisualStudio.Options;
using Obfy.VisualStudio.Services;

namespace Obfy.VisualStudio;

/// <summary>
/// This is the main package class for the Obfy Visual Studio extension.
/// It initializes commands, services, and options.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(GeneralOptionsPage), "Obfy", "General", 0, 0, true)]
[ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class ObfyPackage : ToolkitPackage
{
    /// <summary>
    /// Package GUID - must match the one in source.extension.vsixmanifest
    /// </summary>
    public const string PackageGuidString = PackageGuids.PackageString;

    /// <summary>
    /// Output service for logging to VS Output window
    /// </summary>
    public static IOutputService? OutputService { get; private set; }

    /// <summary>
    /// Service wrapper for invoking Obfy.Core obfuscation
    /// </summary>
    public static IObfuscationServiceWrapper? ObfuscationService { get; private set; }

    /// <summary>
    /// Service for managing per-project settings
    /// </summary>
    public static IProjectSettingsService? ProjectSettingsService { get; private set; }

    /// <summary>
    /// General options page instance
    /// </summary>
    public static ObfyPackage? Instance { get; private set; }

    /// <summary>
    /// Every obfuscation run in flight (manual and post-build), so closing the solution, disposing the package,
    /// a new build of the project or the Cancel Obfuscation command can stop it
    /// </summary>
    public static ObfuscationRunTracker Runs { get; } = new ObfuscationRunTracker();

    public static GeneralOptionsPage? Options =>
        Instance?.GetDialogPage(typeof(GeneralOptionsPage)) as GeneralOptionsPage;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        // CVST005 throws if this method calls base.InitializeAsync directly: the
        // receiver has no symbol, and the analyzer passes that null to ContainsKey.
        await InitializeBaseAsync(cancellationToken, progress);
        Instance = this;

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await InitializeHostServicesAsync();

        await ObfuscateCommand.InitializeAsync(this);
        await TogglePostBuildCommand.InitializeAsync(this);
        await OpenSettingsCommand.InitializeAsync(this);
        await CancelObfuscationCommand.InitializeAsync(this);

        OutputService!.Info("Obfy extension initialized successfully");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Kill any CLI that is still running instead of leaving it behind after Visual Studio's package unloads.
            Runs.Shutdown();
        }

        base.Dispose(disposing);
    }

    private Task InitializeBaseAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) =>
        base.InitializeAsync(cancellationToken, progress);

    private async Task InitializeHostServicesAsync()
    {
        var output = new OutputService();
        await output.InitializeAsync();
        OutputService = output;

        ObfuscationService = new ObfuscationServiceWrapper(output);
        ProjectSettingsService = new ProjectSettingsService();
        await BuildIntegration.BuildEvents.InitializeAsync(this);
    }
}
