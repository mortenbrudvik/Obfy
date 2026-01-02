using Obfy.Core.Models;

namespace Obfy.Console.Wizard;

/// <summary>
/// Shared state across all wizard steps.
/// </summary>
public class WizardContext
{
    /// <summary>
    /// The settings being configured.
    /// </summary>
    public ObfySettings Settings { get; set; } = new();

    /// <summary>
    /// Output file path for the configuration.
    /// </summary>
    public FileInfo OutputFile { get; set; } = new("obfy.json");

    /// <summary>
    /// Whether running in quick mode (skip advanced options).
    /// </summary>
    public bool IsQuickMode { get; set; }

    /// <summary>
    /// The type of application being protected.
    /// </summary>
    public string UseCase { get; set; } = "Desktop Application";

    /// <summary>
    /// Whether this is a public API (library, NuGet package).
    /// </summary>
    public bool IsPublicApi { get; set; }

    /// <summary>
    /// Whether the wizard was cancelled.
    /// </summary>
    public bool Cancelled { get; set; }
}
