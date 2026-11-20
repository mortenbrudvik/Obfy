namespace Obfy.UI.Help;

public static class HelpCatalog
{
    public const string GettingStartedId = "getting-started";
    public const string FilesOutputId = "files-output";
    public const string SettingsLevelsId = "settings-levels";
    public const string TechniquesId = "techniques";
    public const string ShortcutsId = "shortcuts";
    public const string ResultsId = "results";

    public const string DropAssembliesPrompt = "Drag and drop assemblies or a solution";
    public const string SupportedInputExtensions = ".dll, .exe, .cs, .sln, .slnx, .csproj, .vbproj, .fsproj";
    public const string SingleOutputPlaceholder = "Next to the input as *.obfuscated.*";
    public const string ClosedSetOutputPlaceholder = "obfy-out folder next to the first input";
    public const string MergeOutputPlaceholder = "Next to the first input as *.obfuscated.*";

    public const string ConfidentialitySentence =
        "String, constant, resource, method, and packing encryption is obfuscation, not secrecy: the keys live in the output assembly (or the native packing overlay) and are recoverable by anyone who runs or inspects it.";

    public static IReadOnlyList<HelpTopic> Create() =>
    [
        new HelpTopic(GettingStartedId, "Getting started",
        [
            new HelpParagraph($"Add .dll, .exe, or .cs files, or a solution or project, with Add Files. You can also drop them on the window. The empty state says “{DropAssembliesPrompt}” and “{SupportedInputExtensions}”."),
            new HelpParagraph("Pick an obfuscation level, then click Obfuscate (Ctrl+Enter). The closed dropdown shows Minimal, Standard, Aggressive, or Custom. The line under it explains that preset."),
            new HelpParagraph($"Output goes to Output Directory. When that box is empty, one file is written {SingleOutputPlaceholder.TrimEnd('.')} A closed set (two or more assemblies, or files loaded from a solution or project) is written to an {ClosedSetOutputPlaceholder}. Merge writes {MergeOutputPlaceholder.TrimEnd('.')}"),
            new HelpParagraph(ConfidentialitySentence)
        ]),
        new HelpTopic(FilesOutputId, "Files & output",
        [
            new HelpParagraph("Add Files (Ctrl+O) opens a multi-select picker for assemblies, C# source, solutions, and projects. Clear removes the whole list. Remove file on a row removes that file, and the button name includes the file name. Delete removes the extended selection. The include checkbox drops a file from the run without removing the row, and can bring a skipped row back."),
            new HelpParagraph($"You can drag and drop onto the window, not only Input Files. The empty state says “{DropAssembliesPrompt}” / “{SupportedInputExtensions}”. Drop a .json file to load that configuration."),
            new HelpParagraph("A closed set is two or more included assemblies, or any file that came from a solution or project. Those files are protected together. Skipped rows stay in the list with their reason and are not obfuscated."),
            new HelpParagraph("Set Output Directory (or Browse output directory). The placeholder names where an empty box will write. Open output folder opens the last written output, or Output Directory when that folder exists. If the folder is missing, the button stays disabled and the tooltip says the folder was not found."),
            new HelpParagraph("Save Configuration (Ctrl+S) and Load Configuration (Ctrl+L) write and read a JSON settings file. The save dialog defaults to obfy-config.json. A session -o output directory is not saved as the default."),
            new HelpParagraph("Write symbol map after obfuscation writes symbolmap.json in Output Directory, or next to the input if that box is empty. The symbol map path box overrides that location. This is automatic after a run; Export Map on Obfuscation Results is a separate save-as."),
            new HelpParagraph("Merge input assemblies combines the files in Input Files into one output. Embed referenced DLLs packs sibling referenced DLLs into the result. Include and exclude patterns for embedding are on the Merge and embedding card.")
        ]),
        new HelpTopic(SettingsLevelsId, "Settings & levels",
        [
            new HelpParagraph("Obfuscation level is Minimal — symbol renaming only; Standard — rename, encrypt strings, strip metadata; Aggressive — all protections at high intensity; or Custom. The closed dropdown shows only the level name. That explanation is the line under the dropdown and the text inside the open list."),
            new HelpParagraph("Choosing Minimal, Standard, or Aggressive fills the technique switches so you can see what that preset turned on. The switch stays in the card header when the card is collapsed. Change any setting and Obfuscation level switches to Custom. Choose Custom again to restore the choices you had before the preset."),
            new HelpParagraph("Runtime profile is Default, NativeAOT, Unity IL2CPP, or Blazor WebAssembly. NativeAOT, Unity IL2CPP, and Blazor WebAssembly disable Method IL encryption, Anti-dump, dependency embedding, and virtualization. Those switches show “Not available for NativeAOT” (or the profile you picked) and keep the stored value, so switching back to Default restores them. Anti-debug stays available and omits kernel32 P/Invoke.")
        ]),
        new HelpTopic(TechniquesId, "Techniques",
        [
            new HelpNamedNote("String Encryption", "Encrypts string literals (XOR or AES-256). This is obfuscation, not secrecy."),
            new HelpNamedNote("Constant Encryption", "Encrypts numeric constants (int, long, float, double) with XOR or AES-256."),
            new HelpNamedNote("Control Flow", "Rewrites method flow with switch flattening, opaque predicates, or both."),
            new HelpNamedNote("Symbol Renaming", "Renames types, methods, fields, properties, parameters, events, and namespaces."),
            new HelpNamedNote("Protection", "Anti-Debug, Anti-Tamper, Anti-Dump, Anti-Decompiler, Reference Proxy, and Method IL Encryption."),
            new HelpNamedNote("Metadata", "Remove Debug Info, Remove Attributes, and Strip Documentation."),
            new HelpNamedNote("Watermark", "Embeds a Customer / build id. The id is required when Watermark is on. Obfuscate stays disabled until the id is filled, and the box is marked invalid."),
            new HelpNamedNote("Native packer (win-x64)", "Replaces the output with a native Windows host (framework-dependent). The overlay holds the AES-256-CBC key. Set packing.rid to portable for the managed launcher."),
            new HelpNamedNote("Virtualize methods", "Replaces eligible methods with a bytecode interpreter. No EH, generics, byref, custom structs, or constructors. Gated off NativeAOT / IL2CPP / Blazor WASM. Off in every preset. Not confidentiality."),
            new HelpNamedNote("Strong-Name Signing", "Re-signs the output with a Key file (.snk / .pfx). Browse picks the key. A missing file is flagged."),
            new HelpNamedNote("Resource Encryption", "Encrypts embedded resources (XOR or AES-256). This is obfuscation, not secrecy."),
            new HelpNamedNote("Assembly Merge", "Merge input assemblies into one output, and optionally Embed referenced DLLs."),
            new HelpNamedNote("Exclusions", "Namespaces, types, and methods you do not want renamed or transformed. Inclusions are the matching allow-list.")
        ]),
        new HelpTopic(ShortcutsId, "Shortcuts",
        [
            new HelpParagraph("These shortcuts work in the main window."),
            new HelpShortcut("Ctrl+O", "Add Files"),
            new HelpShortcut("Ctrl+S", "Save Configuration"),
            new HelpShortcut("Ctrl+L", "Load Configuration"),
            new HelpShortcut("Ctrl+Enter", "Obfuscate"),
            new HelpShortcut("Esc", "Cancel"),
            new HelpShortcut("F1", "Help")
        ]),
        new HelpTopic(ResultsId, "Results",
        [
            new HelpParagraph("Output, Results, and Preview are tabs under Input Files. Results opens after a run that produced at least one successful output."),
            new HelpParagraph("The summary lists every non-zero counter, grouped as Renamed, Encrypted, Protection, and Metadata, plus Total."),
            new HelpParagraph("Symbols lists original names with members nested under their type. Search waits briefly, then filters that tree. Preview decompiles the assembly you select and shows that file’s name. It does not decompile at the end of the run."),
            new HelpParagraph("Copy is enabled when a symbol is selected. It copies the selected mapping. Export Map writes the symbol map (save dialog, default symbolmap.json). Export Report writes the run report.")
        ])
    ];
}
