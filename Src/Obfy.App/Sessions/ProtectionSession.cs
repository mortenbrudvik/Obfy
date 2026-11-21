using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.Core.Models;

namespace Obfy.App.Sessions;

/// <summary>
/// The protection settings, level presets and everything the page needs to draw them: lifted from the WPF
/// <c>SettingsViewModel</c> without WPF or property-change plumbing. A singleton; the page reads a
/// <see cref="SettingsSnapshot"/> and sends patches, and no rule lives in the page.
/// <para>
/// State is one <see cref="ObfySettings"/> working copy. A loaded configuration is cloned into it whole, so fields no
/// option edits survive load, run and save. Every mutation runs under a lock and raises <see cref="Changed"/> after it,
/// outside the lock, once.
/// </para>
/// <para>
/// The level is Custom exactly when a preset-governed option was edited by hand. Options that are independent of the
/// level (runtime profile, watermark, signing, packing, merge, embedding, incremental cache, the inclusion and
/// exclusion lists) neither flip it nor are touched by a Custom restore.
/// </para>
/// </summary>
public sealed class ProtectionSession
{
    private static readonly IReadOnlyList<TechniqueDef> Techniques =
    [
        new("dependencyEmbedding", GatedByProfile: true),
        new("stringEncryption"),
        new("constantEncryption"),
        new("resourceEncryption"),
        new("antiDebug"),
        new("antiDump", GatedByProfile: true),
        new("antiDecompiler"),
        new("watermark"),
        new("antiTamper"),
        new("virtualization", GatedByProfile: true),
        new("methodEncryption", GatedByProfile: true),
        new("controlFlow"),
        new("referenceProxy"),
        new("symbolRenaming"),
        new("metadataRemoval"),
        new("assemblyMerge"),
        new("signing"),
        new("packing", GatedByProfile: true),
        new("incremental"),
    ];

    private readonly object _gate = new();
    private ObfySettings _settings = new();
    private ObfySettings? _customSnapshot;
    private ObfuscationLevel _basePreset = ObfuscationLevel.Standard;
    private string? _configPath;
    private bool _dirty;
    private long _revision;
    private readonly IEventPublisher? _events;

    /// <param name="events">Where <c>settings.changed</c> goes after every change; null for a session nobody listens to.</param>
    public ProtectionSession(IEventPublisher? events = null) => _events = events;

    /// <summary>Raised once after any change, on the thread that made it, outside the session's lock.</summary>
    public event Action? Changed;

    /// <summary>
    /// After a change: pushes <c>settings.changed</c> with the new snapshot, then raises <see cref="Changed"/>.
    /// Always outside the lock.
    /// </summary>
    private void RaiseChanged()
    {
        _events?.Publish(AppEvents.SettingsChanged, GetSnapshot());
        Changed?.Invoke();
    }

    public ObfuscationLevel Level
    {
        get
        {
            lock (_gate)
                return _settings.Level;
        }
    }

    /// <summary>Applies one option; see <see cref="Update"/>.</summary>
    public void UpdateSetting(string key, object? value) =>
        Update(new Dictionary<string, object?> { [key] = value });

    /// <summary>
    /// Applies a patch of <c>id.name</c> keys (for example <c>controlFlow.enabled</c>, <c>runtimeProfile</c>,
    /// <c>exclusions.types</c>) to values: bool, whole number, text (enum names, case-insensitive) or a list of text.
    /// All-or-nothing: an unknown key or a bad value throws before anything changes.
    /// </summary>
    /// <exception cref="ObfyApiException">An unknown key or a value of the wrong type.</exception>
    public void Update(IReadOnlyDictionary<string, object?> patch)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var (key, _) in patch)
            {
                if (!SettingFields.ByKey.ContainsKey(key))
                    throw new ObfyApiException("settings.unknownField", $"Unknown setting: {key}");
            }

            // Work on a copy so a bad value half-way through leaves the session as it was.
            var working = _settings.Clone();
            foreach (var (key, value) in patch)
                SettingFields.ByKey[key].Write(working, value);

            // The proxy rule: proxying external calls needs the proxy on, and turning the proxy off clears it.
            // A patch that touches the proxy switch decides; otherwise the external-calls switch pulls it on.
            if (patch.ContainsKey("referenceProxy.enabled") && !working.Protection.ReferenceProxy)
                working.Protection.ProxyExternalCalls = false;
            else if (working.Protection.ProxyExternalCalls)
                working.Protection.ReferenceProxy = true;

            // Judge by the result, so a patch that nets out to nothing changes nothing.
            var differing = SettingFields.All.Where(f => !f.SameValue(_settings, working)).ToList();
            changed = differing.Count > 0;
            var governedChanged = differing.Any(f => !f.LevelIndependent);
            if (!changed)
                return;

            _settings = working;
            if (governedChanged)
                MarkCustom();
            _dirty = true;
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Selects a level. A preset replaces the preset-governed options (and resets packing, incremental cache and
    /// virtualization, which no preset turns on); leaving Custom first remembers the choices. Selecting Custom from a
    /// preset restores the remembered choices but keeps every level-independent option as it is now.
    /// </summary>
    public void ApplyLevel(ObfuscationLevel level)
    {
        lock (_gate)
        {
            var previous = _settings.Level;
            if (level == ObfuscationLevel.Custom)
            {
                if (previous == ObfuscationLevel.Custom)
                    return;
                if (_customSnapshot is not null)
                    _settings = RestoreCustomChoices(_customSnapshot, _settings);
                _settings.Level = ObfuscationLevel.Custom;
            }
            else
            {
                if (previous == ObfuscationLevel.Custom)
                    CaptureCustomSnapshot();
                _settings.Level = level;
                _settings.ApplyLevel();
                _basePreset = level;
            }

            _dirty = true;
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>A copy of the settings to hand to the engine or write to a file, level included.</summary>
    public ObfySettings ToObfySettings()
    {
        lock (_gate)
            return _settings.Clone();
    }

    /// <summary>
    /// Loads a configuration. The whole object is kept (cloned), so values no option edits survive the next
    /// <see cref="ToObfySettings"/>. A Custom configuration becomes the Custom snapshot. A configuration whose level
    /// is a preset but whose preset-governed options are not exactly that preset's becomes Custom (see below); the file
    /// itself is not touched.
    /// </summary>
    /// <param name="configPath">The file it came from, or null when it did not come from a file.</param>
    public void FromObfySettings(ObfySettings settings, string? configPath = null)
    {
        lock (_gate)
        {
            var loaded = settings.Clone();
            Normalize(loaded);
            var preset = loaded.Level;

            // The engine re-applies a non-Custom level on every run, overwriting every preset-governed option (packing,
            // incremental cache and virtualization included, which no preset turns on). A file whose options are not
            // exactly its preset's would run as the preset while the screen showed the file's options, so it is Custom,
            // as if the user had edited the options here. The command line treats a configuration file the same way.
            if (preset != ObfuscationLevel.Custom && DiffersFromPreset(loaded, preset))
                loaded.Level = ObfuscationLevel.Custom;

            _settings = loaded;
            _customSnapshot = loaded.Level == ObfuscationLevel.Custom ? loaded.Clone() : null;
            _basePreset = preset == ObfuscationLevel.Custom ? ObfuscationLevel.Standard : preset;
            _configPath = configPath;
            _dirty = false;
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>Records that the current settings were written to <paramref name="configPath"/>.</summary>
    public void MarkSaved(string configPath)
    {
        lock (_gate)
        {
            _configPath = configPath;
            _dirty = false;
            _revision++;
        }

        RaiseChanged();
    }

    /// <summary>
    /// What is wrong with the settings as they stand (a watermark with no id, a missing signing key, ...): the same list
    /// the snapshot carries, without building the rest of it. A run is blocked while this is not empty.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors()
    {
        lock (_gate)
            return Validate(_settings);
    }

    public string? ConfigPath
    {
        get
        {
            lock (_gate)
                return _configPath;
        }
    }

    public SettingsSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var settings = _settings;
            var gated = ProfileGatesIlProtections(settings.RuntimeProfile);
            var reason = gated ? $"Not available for {ProfileDisplayName(settings.RuntimeProfile)}" : null;
            var reference = PresetReference(settings, _basePreset);

            var techniques = Techniques.Select(def => BuildTechnique(def, settings, reference, gated, reason)).ToList();
            return new SettingsSnapshot(
                _revision,
                settings.Level.ToString(),
                LevelSummary(settings.Level),
                techniques,
                Validate(settings),
                _configPath,
                _dirty,
                settings.RuntimeProfile.ToString(),
                new NameLists(settings.Exclusions.Namespaces.ToArray(), settings.Exclusions.Types.ToArray(), settings.Exclusions.Methods.ToArray()),
                new NameLists(settings.Inclusions.Namespaces.ToArray(), settings.Inclusions.Types.ToArray(), settings.Inclusions.Methods.ToArray()),
                new SettingsChoices(
                    Enum.GetNames<ObfuscationLevel>(),
                    Enum.GetNames<EncryptionAlgorithm>(),
                    Enum.GetNames<ControlFlowMode>(),
                    Enum.GetNames<NamingMode>(),
                    Enum.GetNames<RuntimeProfile>(),
                    PackingRids));
        }
    }

    private static readonly string[] PackingRids = ["win-x64", "portable"];

    /// <summary>A preset-governed option changed: the level is now Custom, and the choices are remembered.</summary>
    private void MarkCustom()
    {
        _settings.Level = ObfuscationLevel.Custom;
        CaptureCustomSnapshot();
    }

    private void CaptureCustomSnapshot()
    {
        var snapshot = _settings.Clone();
        snapshot.Level = ObfuscationLevel.Custom;
        _customSnapshot = snapshot;
    }

    /// <summary>
    /// The remembered choices with the current level-independent options laid over them, so selecting Custom never
    /// reverts the profile, watermark, signing, packing target (rid), merge, embedding or the name lists.
    /// Packing, incremental cache and virtualization are governed, not level-independent: they come back with the snapshot.
    /// </summary>
    private static ObfySettings RestoreCustomChoices(ObfySettings snapshot, ObfySettings current)
    {
        var restored = snapshot.Clone();
        restored.PostBuildEnabled = current.PostBuildEnabled;
        restored.RuntimeProfile = current.RuntimeProfile;
        CopyProperties(current.Signing, restored.Signing);
        restored.Packing.Rid = current.Packing.Rid; // packing.enabled is governed and comes back with the snapshot
        CopyProperties(current.AssemblyMerge, restored.AssemblyMerge);
        CopyProperties(current.DependencyEmbedding, restored.DependencyEmbedding);
        CopyProperties(current.Watermark, restored.Watermark);
        CopyProperties(current.Exclusions, restored.Exclusions);
        CopyProperties(current.Inclusions, restored.Inclusions);
        return restored;
    }

    private static void CopyProperties<T>(T from, T to) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
                continue;
            var value = property.GetValue(from);
            property.SetValue(to, value is List<string> list ? new List<string>(list) : value);
        }
    }

    /// <summary>Fills what a hand-edited file may leave null or blank, so the options always read a value.</summary>
    private static void Normalize(ObfySettings settings)
    {
        settings.Exclusions.Namespaces ??= [];
        settings.Exclusions.Types ??= [];
        settings.Exclusions.Methods ??= [];
        settings.Inclusions.Namespaces ??= [];
        settings.Inclusions.Types ??= [];
        settings.Inclusions.Methods ??= [];
        settings.DependencyEmbedding.IncludePatterns ??= [];
        settings.DependencyEmbedding.ExcludePatterns ??= [];
        if (string.IsNullOrWhiteSpace(settings.Packing.Rid))
            settings.Packing.Rid = "win-x64";
    }

    /// <summary>The settings with <paramref name="preset"/> applied: what a run of that level would use.</summary>
    private static ObfySettings PresetReference(ObfySettings settings, ObfuscationLevel preset)
    {
        var reference = settings.Clone();
        reference.Level = preset;
        reference.ApplyLevel();
        return reference;
    }

    /// <summary>One rule for "this option is not what the preset gives": the Changed badge and the config load use it.</summary>
    private static bool DiffersFromReference(SettingField field, ObfySettings settings, ObfySettings reference) =>
        !field.LevelIndependent && !field.SameValue(settings, reference);

    private static bool DiffersFromPreset(ObfySettings settings, ObfuscationLevel preset)
    {
        var reference = PresetReference(settings, preset);
        return SettingFields.All.Any(f => DiffersFromReference(f, settings, reference));
    }

    private static TechniqueState BuildTechnique(TechniqueDef def, ObfySettings settings, ObfySettings reference, bool gated, string? reason)
    {
        var fields = SettingFields.All.Where(f => f.Group == def.Id).ToList();
        var options = new Dictionary<string, object?>();
        foreach (var field in fields.Where(f => f.Name != "enabled"))
            options[field.Name] = field.Read(settings);

        var enabled = fields.FirstOrDefault(f => f.Name == "enabled")?.Read(settings) is true
            // metadataRemoval has no switch of its own: it is on while any of its options is.
            || (def.Id == "metadataRemoval" && options.Values.Any(v => v is true));

        var unavailable = def.GatedByProfile && gated;
        var differs = fields.Any(f => DiffersFromReference(f, settings, reference));
        return new TechniqueState(def.Id, enabled, !unavailable, unavailable ? reason : null, differs, options);
    }

    private static IReadOnlyList<ValidationError> Validate(ObfySettings settings)
    {
        var errors = new List<ValidationError>();

        if (settings.Watermark.Enabled && string.IsNullOrWhiteSpace(settings.Watermark.Id))
            errors.Add(new ValidationError("watermark.id", "Watermark is enabled but no id was specified."));

        if (settings.Signing.Enabled)
        {
            var keyFile = settings.Signing.KeyFile;
            if (string.IsNullOrWhiteSpace(keyFile))
            {
                errors.Add(new ValidationError("signing.keyFile", "Signing is enabled but no key file was specified."));
            }
            else
            {
                if (!File.Exists(keyFile))
                    errors.Add(new ValidationError("signing.keyFile", $"The key file was not found: {keyFile}"));
                if ((keyFile.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || keyFile.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
                    && string.IsNullOrWhiteSpace(settings.Signing.PasswordEnvironmentVariable))
                    errors.Add(new ValidationError(
                        "signing.passwordEnvironmentVariable",
                        "PFX signing needs the name of an environment variable that holds the password."));
            }
        }

        if (settings.Packing.Enabled && !settings.Packing.IsNativeWin64 && !settings.Packing.IsPortable)
            errors.Add(new ValidationError("packing.rid", "The packing target must be win-x64 or portable."));

        return errors;
    }

    private static bool ProfileGatesIlProtections(RuntimeProfile profile) =>
        profile is RuntimeProfile.NativeAot or RuntimeProfile.UnityIl2Cpp or RuntimeProfile.BlazorWasm;

    private static string ProfileDisplayName(RuntimeProfile profile) => profile switch
    {
        RuntimeProfile.NativeAot => "NativeAOT",
        RuntimeProfile.UnityIl2Cpp => "Unity IL2CPP",
        RuntimeProfile.BlazorWasm => "Blazor WebAssembly",
        _ => "Default"
    };

    private static string LevelSummary(ObfuscationLevel level) => level switch
    {
        ObfuscationLevel.Minimal => "Rename symbols and strip debug information.",
        ObfuscationLevel.Standard => "Rename symbols, encrypt strings, and strip metadata.",
        ObfuscationLevel.Aggressive => "All protections at high intensity.",
        _ => "Your technique choices. Select a preset to replace them, then choose Custom to restore this set."
    };

    private sealed record TechniqueDef(string Id, bool GatedByProfile = false);
}
