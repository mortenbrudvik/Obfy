using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Obfy.VisualStudio.Services;

namespace Obfy.VisualStudio.Options;

/// <summary>
/// Options page that appears in Tools > Options > Obfy > General
/// </summary>
[ComVisible(true)]
[Guid("8d1e2f3a-4b5c-6d7e-8f9a-0b1c2d3e4f5a")]
public class GeneralOptionsPage : DialogPage
{
    private ObfuscationLevel _defaultLevel = ObfuscationLevel.Standard;
    private bool _enablePostBuildByDefault = false;
    private bool _releaseOnly = true;
    private bool _generateSymbolMap = false;
    private bool _showOutputWindow = true;

    /// <summary>
    /// Default obfuscation level for new projects
    /// </summary>
    [Category("General")]
    [DisplayName("Default Level")]
    [Description("Default obfuscation level for new projects")]
    [DefaultValue(ObfuscationLevel.Standard)]
    public ObfuscationLevel DefaultLevel
    {
        get => _defaultLevel;
        set => _defaultLevel = value;
    }

    /// <summary>
    /// Enable post-build obfuscation by default for new projects
    /// </summary>
    [Category("Build Integration")]
    [DisplayName("Enable Post-Build by Default")]
    [Description("Enable post-build obfuscation by default when creating new project settings")]
    [DefaultValue(false)]
    public bool EnablePostBuildByDefault
    {
        get => _enablePostBuildByDefault;
        set => _enablePostBuildByDefault = value;
    }

    /// <summary>
    /// Only run post-build obfuscation for Release configuration
    /// </summary>
    [Category("Build Integration")]
    [DisplayName("Release Only")]
    [Description("Only obfuscate in Release configuration (skip Debug builds)")]
    [DefaultValue(true)]
    public bool ReleaseOnly
    {
        get => _releaseOnly;
        set => _releaseOnly = value;
    }

    /// <summary>
    /// Generate symbol map after obfuscation
    /// </summary>
    [Category("Output")]
    [DisplayName("Generate Symbol Map")]
    [Description("Generate symbol map file after obfuscation")]
    [DefaultValue(false)]
    public bool GenerateSymbolMap
    {
        get => _generateSymbolMap;
        set => _generateSymbolMap = value;
    }

    /// <summary>
    /// Show output window during obfuscation
    /// </summary>
    [Category("Output")]
    [DisplayName("Show Output Window")]
    [Description("Automatically show the Output window when obfuscation starts")]
    [DefaultValue(true)]
    public bool ShowOutputWindow
    {
        get => _showOutputWindow;
        set => _showOutputWindow = value;
    }
}
