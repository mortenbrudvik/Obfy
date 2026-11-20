using System.IO;
using CommunityToolkit.Mvvm.Input;
using Obfy.Core.Models;
using Obfy.UI.Services;

namespace Obfy.UI.ViewModels;

public partial class SettingsViewModel
{
    private readonly IFileDialogService? _fileDialogService;
    private ObfySettings? _customSnapshot;
    private ObfuscationLevel _previousLevel = ObfuscationLevel.Standard;
    private bool _suppressCustomRestore;

    private static readonly HashSet<string> PresetNeutralProperties = new(StringComparer.Ordinal)
    {
        nameof(Level),
        nameof(LevelSummary),
        nameof(NewExcludedNamespace),
        nameof(NewExcludedType),
        nameof(NewExcludedMethod),
        nameof(NewIncludedNamespace),
        nameof(NewIncludedType),
        nameof(NewIncludedMethod),
        nameof(NewEmbeddingInclude),
        nameof(NewEmbeddingExclude),
        nameof(WatermarkIdInvalid),
        nameof(SigningKeyMissing),
        nameof(ProfileGatesIlProtections),
        nameof(MethodEncryptionAvailable),
        nameof(AntiDumpAvailable),
        nameof(DependencyEmbeddingAvailable),
        nameof(VirtualizationAvailable),
        nameof(ProfileUnavailableNote),
        nameof(MethodEncryptionNote),
        nameof(AntiDumpNote),
        nameof(DependencyEmbeddingNote),
        nameof(VirtualizationNote),
        nameof(AntiDebugNote)
    };

    public SettingsViewModel(IFileDialogService? fileDialogService = null)
    {
        _fileDialogService = fileDialogService;
    }

    public static string[] AvailablePackingRids { get; } = ["win-x64", "portable"];

    public string LevelSummary => Level switch
    {
        ObfuscationLevel.Minimal => "Symbol renaming only.",
        ObfuscationLevel.Standard => "Rename symbols, encrypt strings, and strip metadata.",
        ObfuscationLevel.Aggressive => "All protections at high intensity.",
        _ => "Your technique choices. Select a preset to replace them, then choose Custom to restore this set."
    };

    public bool WatermarkIdInvalid => WatermarkEnabled && string.IsNullOrWhiteSpace(WatermarkId);

    public bool SigningKeyMissing =>
        SigningEnabled
        && !string.IsNullOrWhiteSpace(SigningKeyFile)
        && !File.Exists(SigningKeyFile);

    public bool ProfileGatesIlProtections =>
        RuntimeProfile is RuntimeProfile.NativeAot or RuntimeProfile.UnityIl2Cpp or RuntimeProfile.BlazorWasm;

    public bool MethodEncryptionAvailable => !ProfileGatesIlProtections;
    public bool AntiDumpAvailable => !ProfileGatesIlProtections;
    public bool DependencyEmbeddingAvailable => !ProfileGatesIlProtections;
    public bool VirtualizationAvailable => !ProfileGatesIlProtections;

    public string ProfileUnavailableNote => ProfileGatesIlProtections
        ? $"Not available for {ProfileDisplayName}"
        : string.Empty;

    public string MethodEncryptionNote => MethodEncryptionAvailable
        ? "XOR-encrypts method IL in the PE. Windows only. Obfuscation, not secrecy."
        : ProfileUnavailableNote;

    public string AntiDumpNote => AntiDumpAvailable
        ? "Wipes PE headers in memory and patches MiniDumpWriteDump in-process. Windows x86/x64. External dumpers are unaffected."
        : ProfileUnavailableNote;

    public string DependencyEmbeddingNote => DependencyEmbeddingAvailable
        ? "Packs sibling referenced DLLs as embedded resources."
        : ProfileUnavailableNote;

    public string VirtualizationNote => VirtualizationAvailable
        ? "Replaces eligible methods with a bytecode interpreter. Skips EH, generics, byref, custom structs, and constructors. Off in every preset. Not confidentiality."
        : ProfileUnavailableNote;

    public string AntiDebugNote => ProfileGatesIlProtections
        ? "Detects a debugger. On NativeAOT, IL2CPP, and Blazor WebAssembly this stays available and omits kernel32 P/Invoke."
        : "Injects debugger detection.";

    private string ProfileDisplayName => RuntimeProfile switch
    {
        RuntimeProfile.NativeAot => "NativeAOT",
        RuntimeProfile.UnityIl2Cpp => "Unity IL2CPP",
        RuntimeProfile.BlazorWasm => "Blazor WebAssembly",
        _ => "Default"
    };

    partial void OnRuntimeProfileChanged(RuntimeProfile value) => NotifyProfileGates();

    partial void OnWatermarkEnabledChanged(bool value) => OnPropertyChanged(nameof(WatermarkIdInvalid));

    partial void OnWatermarkIdChanged(string value) => OnPropertyChanged(nameof(WatermarkIdInvalid));

    partial void OnSigningEnabledChanged(bool value) => OnPropertyChanged(nameof(SigningKeyMissing));

    partial void OnSigningKeyFileChanged(string value) => OnPropertyChanged(nameof(SigningKeyMissing));

    [RelayCommand]
    private void BrowseSigningKey()
    {
        var path = _fileDialogService?.ShowOpenKeyFileDialog();
        if (!string.IsNullOrEmpty(path))
            SigningKeyFile = path;
    }

    private void CaptureCustomSnapshot()
    {
        var snapshot = ToObfySettings();
        snapshot.Level = ObfuscationLevel.Custom;
        _customSnapshot = snapshot;
    }

    private void NotifyProfileGates()
    {
        OnPropertyChanged(nameof(ProfileGatesIlProtections));
        OnPropertyChanged(nameof(MethodEncryptionAvailable));
        OnPropertyChanged(nameof(AntiDumpAvailable));
        OnPropertyChanged(nameof(DependencyEmbeddingAvailable));
        OnPropertyChanged(nameof(VirtualizationAvailable));
        OnPropertyChanged(nameof(ProfileUnavailableNote));
        OnPropertyChanged(nameof(MethodEncryptionNote));
        OnPropertyChanged(nameof(AntiDumpNote));
        OnPropertyChanged(nameof(DependencyEmbeddingNote));
        OnPropertyChanged(nameof(VirtualizationNote));
        OnPropertyChanged(nameof(AntiDebugNote));
    }
}
