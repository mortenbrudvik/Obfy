namespace Obfy.UI.Help;

public static class HelpCatalog
{
    public const string GettingStartedId = "getting-started";
    public const string FilesOutputId = "files-output";
    public const string SettingsLevelsId = "settings-levels";
    public const string TechniquesId = "techniques";
    public const string ShortcutsId = "shortcuts";
    public const string ResultsId = "results";

    public const string ConfidentialitySentence =
        "String, constant, resource, and method encryption is obfuscation, not secrecy: the keys live in the output assembly and are recoverable by anyone who runs or inspects it.";

    public static IReadOnlyList<HelpTopic> Create() =>
    [
        new HelpTopic(GettingStartedId, "Getting started",
        [
            new HelpParagraph("Add .dll, .exe, or .cs files with Add Files, or drop them onto Input Files."),
            new HelpParagraph("Pick an Obfuscation level, then click Obfuscate (Ctrl+Enter)."),
            new HelpParagraph("Output goes to Output Directory. If that box is empty, files are written next to the input as *.obfuscated.*."),
            new HelpParagraph(ConfidentialitySentence)
        ]),
        new HelpTopic(FilesOutputId, "Files & output",
        [
            new HelpParagraph("Add Files (Ctrl+O) opens a multi-select picker for .dll, .exe, and .cs. Clear removes the whole list; Remove file on a row removes one file."),
            new HelpParagraph("You can drag and drop assemblies onto Input Files. The empty state says “Drag and drop assemblies here” / “or click Add Files (.dll, .exe, .cs)”."),
            new HelpParagraph("Set Output Directory (or Browse output directory). The toolbar Open output folder button opens the folder of the last written output."),
            new HelpParagraph("Save Configuration (Ctrl+S) and Load Configuration (Ctrl+L) write and read a JSON settings file. The save dialog defaults to obfy-config.json."),
            new HelpParagraph("Write symbol map after obfuscation writes symbolmap.json in Output Directory, or next to the input if that box is empty. This is automatic after a run; Export Map on Obfuscation Results is a separate save-as."),
            new HelpParagraph("Merge input assemblies combines the files in Input Files into one output. Embed referenced DLLs packs sibling referenced DLLs into the result. Use the Assembly Merge expander; this is not a merge tutorial.")
        ]),
        new HelpTopic(SettingsLevelsId, "Settings & levels",
        [
            new HelpParagraph("Obfuscation level is Minimal — symbol renaming only; Standard — rename, encrypt strings, strip metadata; Aggressive — all protections at high intensity; or Custom."),
            new HelpParagraph("Choosing Minimal, Standard, or Aggressive fills the expanders so you can see what that preset turned on. Change any setting and Obfuscation level switches to Custom. Custom is how you edit techniques freely; the expanders stay readable on a preset."),
            new HelpParagraph("Runtime profile is Default, NativeAOT, Unity IL2CPP, or Blazor WebAssembly. NativeAOT, Unity IL2CPP, and Blazor WebAssembly change what Protection can do (Method IL Encryption, Anti-Dump, and embedding are gated; Anti-Debug stays on without kernel32 P/Invoke). Pick the profile that matches how the output will run.")
        ]),
        new HelpTopic(TechniquesId, "Techniques",
        [
            new HelpNamedNote("String Encryption", "Encrypts string literals (XOR or AES-256). This is obfuscation, not secrecy."),
            new HelpNamedNote("Constant Encryption", "Encrypts numeric constants (int, long, float, double) with XOR or AES-256."),
            new HelpNamedNote("Control Flow", "Rewrites method flow with switch flattening, opaque predicates, or both."),
            new HelpNamedNote("Symbol Renaming", "Renames types, methods, fields, properties, parameters, events, and namespaces."),
            new HelpNamedNote("Protection", "Anti-Debug, Anti-Tamper, Anti-Dump, Anti-Decompiler, Reference Proxy, and Method IL Encryption."),
            new HelpNamedNote("Metadata", "Remove Debug Info, Remove Attributes, and Strip Documentation."),
            new HelpNamedNote("Watermark", "Embeds a Customer / build id. The id is required when Watermark is on."),
            new HelpNamedNote("Managed launcher", "Pack managed launcher, Incremental cache, and Virtualize simple methods."),
            new HelpNamedNote("Strong-Name Signing", "Re-signs the output with a Key file (.snk / .pfx)."),
            new HelpNamedNote("Resource Encryption", "Encrypts embedded resources (XOR or AES-256). This is obfuscation, not secrecy."),
            new HelpNamedNote("Assembly Merge", "Merge input assemblies into one output, and optionally Embed referenced DLLs."),
            new HelpNamedNote("Exclusions", "Namespaces, types, and methods you do not want renamed or transformed.")
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
            new HelpParagraph("Obfuscation Results appears under Input Files after a run."),
            new HelpParagraph("The summary counts Strings, Types, Methods, Fields, Control Flow, and Total."),
            new HelpParagraph("Symbols lists original → obfuscated names. Preview shows a decompile of the last assembly output when one exists."),
            new HelpParagraph("Copy copies the selected mapping. Export Map writes the symbol map (save dialog, default symbolmap.json). Export Report writes the run report.")
        ])
    ];
}
