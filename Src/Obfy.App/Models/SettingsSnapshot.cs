namespace Obfy.App.Models;

/// <summary>
/// Everything the page needs to draw the settings without knowing the rules: the values, which techniques are available
/// and why not, what differs from the chosen level, and what blocks a run. Serialized by <c>JsonUtils</c> (camelCase,
/// enums as strings), so every name here is a name in the page.
/// </summary>
/// <param name="Level">Minimal, Standard, Aggressive or Custom.</param>
/// <param name="LevelSummary">One line saying what the level does.</param>
/// <param name="Techniques">Every protection and output option, each with its own options. Technique ids are camelCase.</param>
/// <param name="Errors">What must be fixed before a run. Field is the patch key of the offending option.</param>
/// <param name="ConfigPath">The configuration file the settings came from or were last saved to, or null.</param>
/// <param name="Dirty">Whether the settings changed since the configuration was loaded or saved.</param>
/// <param name="RuntimeProfile">Default, NativeAot, UnityIl2Cpp or BlazorWasm.</param>
/// <param name="Choices">The values each dropdown offers.</param>
public record SettingsSnapshot(
    string Level,
    string LevelSummary,
    IReadOnlyList<TechniqueState> Techniques,
    IReadOnlyList<ValidationError> Errors,
    string? ConfigPath,
    bool Dirty,
    string RuntimeProfile,
    NameLists Exclusions,
    NameLists Inclusions,
    SettingsChoices Choices);

/// <summary>
/// One technique (or output option such as signing or packing).
/// </summary>
/// <param name="Id">camelCase id, for example <c>stringEncryption</c>.</param>
/// <param name="Enabled">The stored switch. Kept as stored when the technique is unavailable for the runtime profile.</param>
/// <param name="Available">False when the runtime profile rules the technique out; the page disables it.</param>
/// <param name="UnavailableReason">Why, when not available.</param>
/// <param name="DiffersFromLevel">The technique's values are not the ones the level's preset gives.</param>
/// <param name="Options">Every other option of the technique, by camelCase name. A patch key is <c>id.name</c>.</param>
public record TechniqueState(
    string Id,
    bool Enabled,
    bool Available,
    string? UnavailableReason,
    bool DiffersFromLevel,
    IReadOnlyDictionary<string, object?> Options);

/// <param name="Field">The patch key of the option at fault, for example <c>watermark.id</c>.</param>
public record ValidationError(string Field, string Message);

/// <summary>Three pattern lists (namespaces, types, methods) of an exclusion or inclusion rule set.</summary>
public record NameLists(IReadOnlyList<string> Namespaces, IReadOnlyList<string> Types, IReadOnlyList<string> Methods);

/// <summary>The values each dropdown offers, as strings the patch accepts.</summary>
public record SettingsChoices(
    IReadOnlyList<string> Levels,
    IReadOnlyList<string> Algorithms,
    IReadOnlyList<string> ControlFlowModes,
    IReadOnlyList<string> NamingModes,
    IReadOnlyList<string> RuntimeProfiles,
    IReadOnlyList<string> PackingRids);
