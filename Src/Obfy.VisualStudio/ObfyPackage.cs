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
    public const string PackageGuidString = "5a9b8c7d-6e5f-4a3b-2c1d-0e9f8a7b6c5d";

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
    public static GeneralOptionsPage? Options =>
        GetGlobalService(typeof(GeneralOptionsPage)) as GeneralOptionsPage;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        // Switch to main thread for UI operations
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // Initialize services
        OutputService = new OutputService();
        await ((OutputService)OutputService).InitializeAsync();

        ObfuscationService = new ObfuscationServiceWrapper(OutputService);
        ProjectSettingsService = new ProjectSettingsService();

        // Register commands
        await ObfuscateCommand.InitializeAsync(this);
        await TogglePostBuildCommand.InitializeAsync(this);
        await OpenSettingsCommand.InitializeAsync(this);

        // Subscribe to build events
        await BuildIntegration.BuildEvents.InitializeAsync(this);

        OutputService.Info("Obfy extension initialized successfully");
    }
}
