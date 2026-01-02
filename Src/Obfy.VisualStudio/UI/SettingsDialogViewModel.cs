using System.ComponentModel;
using System.Runtime.CompilerServices;
using Obfy.VisualStudio.Services;

namespace Obfy.VisualStudio.UI;

/// <summary>
/// ViewModel for the simplified settings dialog
/// </summary>
public class SettingsDialogViewModel : INotifyPropertyChanged
{
    private ObfuscationLevel _level;
    private bool _postBuildEnabled;
    private bool _antiDebug;
    private bool _antiTamper;
    private bool _antiDecompiler;
    private bool _stringEncryption;
    private bool _controlFlow;
    private bool _symbolRenaming;
    private string _projectName;

    public SettingsDialogViewModel(ObfySettings settings, string projectName)
    {
        _projectName = projectName;

        // Initialize from settings
        _level = settings.Level;
        _postBuildEnabled = settings.PostBuildEnabled;
        _antiDebug = settings.AntiDebug;
        _antiTamper = settings.AntiTamper;
        _antiDecompiler = settings.AntiDecompiler;
        _stringEncryption = settings.StringEncryption;
        _controlFlow = settings.ControlFlow;
        _symbolRenaming = settings.SymbolRenaming;
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public ObfuscationLevel Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
            {
                // When level changes, update the individual toggles to match
                ApplyLevel();
            }
        }
    }

    public bool PostBuildEnabled
    {
        get => _postBuildEnabled;
        set => SetProperty(ref _postBuildEnabled, value);
    }

    public bool AntiDebug
    {
        get => _antiDebug;
        set
        {
            if (SetProperty(ref _antiDebug, value))
            {
                SetLevelToCustom();
            }
        }
    }

    public bool AntiTamper
    {
        get => _antiTamper;
        set
        {
            if (SetProperty(ref _antiTamper, value))
            {
                SetLevelToCustom();
            }
        }
    }

    public bool AntiDecompiler
    {
        get => _antiDecompiler;
        set
        {
            if (SetProperty(ref _antiDecompiler, value))
            {
                SetLevelToCustom();
            }
        }
    }

    public bool StringEncryption
    {
        get => _stringEncryption;
        set
        {
            if (SetProperty(ref _stringEncryption, value))
            {
                SetLevelToCustom();
            }
        }
    }

    public bool ControlFlow
    {
        get => _controlFlow;
        set
        {
            if (SetProperty(ref _controlFlow, value))
            {
                SetLevelToCustom();
            }
        }
    }

    public bool SymbolRenaming
    {
        get => _symbolRenaming;
        set
        {
            if (SetProperty(ref _symbolRenaming, value))
            {
                SetLevelToCustom();
            }
        }
    }

    /// <summary>
    /// Available obfuscation levels
    /// </summary>
    public ObfuscationLevel[] Levels { get; } = new[]
    {
        ObfuscationLevel.Minimal,
        ObfuscationLevel.Standard,
        ObfuscationLevel.Aggressive,
        ObfuscationLevel.Custom
    };

    /// <summary>
    /// Create settings from the current state
    /// </summary>
    public ObfySettings GetSettings()
    {
        return new ObfySettings
        {
            Level = _level,
            PostBuildEnabled = _postBuildEnabled,
            AntiDebug = _antiDebug,
            AntiTamper = _antiTamper,
            AntiDecompiler = _antiDecompiler,
            StringEncryption = _stringEncryption,
            ControlFlow = _controlFlow,
            SymbolRenaming = _symbolRenaming
        };
    }

    private void ApplyLevel()
    {
        var levelSettings = ObfySettings.ForLevel(_level);

        // Temporarily disable the custom level switch
        _antiDebug = levelSettings.AntiDebug;
        _antiTamper = levelSettings.AntiTamper;
        _antiDecompiler = levelSettings.AntiDecompiler;
        _stringEncryption = levelSettings.StringEncryption;
        _controlFlow = levelSettings.ControlFlow;
        _symbolRenaming = levelSettings.SymbolRenaming;

        // Notify all properties changed
        OnPropertyChanged(nameof(AntiDebug));
        OnPropertyChanged(nameof(AntiTamper));
        OnPropertyChanged(nameof(AntiDecompiler));
        OnPropertyChanged(nameof(StringEncryption));
        OnPropertyChanged(nameof(ControlFlow));
        OnPropertyChanged(nameof(SymbolRenaming));
    }

    private void SetLevelToCustom()
    {
        if (_level != ObfuscationLevel.Custom)
        {
            _level = ObfuscationLevel.Custom;
            OnPropertyChanged(nameof(Level));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
