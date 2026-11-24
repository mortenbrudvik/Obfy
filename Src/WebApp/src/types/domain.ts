// Shapes exchanged with the .NET side (Obfy.App request/response records and pushed events): camelCase JSON, string
// enums, Guid ids as strings. Keep each type next to the C# record it mirrors.

/** What `app.info` answers with (AppInfo). */
export interface AppInfo {
  version: string;
}

// ---- Run (Obfy.App/Models/RunSnapshot.cs). Answers `run.get`; pushed with `run.changed` and `run.completed`. ----

/** How a run turns the included files into output (RunMode). */
export type RunMode = "None" | "Single" | "Sources" | "Merge" | "ClosedSet";

/** Where the run stands (RunStatus). */
export type RunStatus = "Idle" | "Running" | "Done" | "Cancelled" | "Faulted";

/** Everything the page needs to draw the run controls (RunSnapshot). */
export interface RunSnapshot {
  /** Rises with every change. `run.completed` carries the same revision as the `run.changed` before it. */
  revision: number;
  /** A run can start now. False while one runs, with nothing included, or with invalid settings. */
  canStart: boolean;
  /** Why `canStart` is false, in plain words; null when it is true. */
  blockedReason: string | null;
  mode: RunMode;
  modeExplanation: string;
  /** Where the output goes. */
  outputHint: string;
  /** Overall progress, 0 to 100. Meaningless while `indeterminate`. */
  progress: number;
  indeterminate: boolean;
  currentFile: string | null;
  completed: number;
  total: number;
  failed: number;
  status: RunStatus;
  /** The status line: what is happening, or how the last run ended. */
  message: string;
}

// ---- Files and output (Obfy.App/Models/InputSnapshot.cs). Answers `files.get`; pushed with `files.changed`. ----

export type FileStatus = "Pending" | "Processing" | "Success" | "Error" | "Skipped";
export type FileKind = "Assembly" | "Source" | "Project";
export type FileOrigin = "Added" | "Solution";
export type NoticeLevel = "Info" | "Warning" | "Error";

/** One row of the file list (FileRow). The page refers to a row by `id` only. */
export interface FileRow {
  id: string;
  name: string;
  path: string;
  origin: FileOrigin;
  kind: FileKind;
  size: number;
  sizeText: string;
  included: boolean;
  status: FileStatus;
  skipReason: string | null;
  outputPath: string | null;
  error: string | null;
}

/** A message about the last thing done to the list (InputNotice). */
export interface InputNotice {
  level: NoticeLevel;
  title: string;
  message: string;
}

/** The file list and the output row (InputSnapshot). */
export interface InputSnapshot {
  revision: number;
  files: FileRow[];
  hasIncludedFiles: boolean;
  outputFolder: string;
  outputHint: string;
  mergeOutputHint: string;
  outputWarning: string | null;
  writeSymbolMap: boolean;
  symbolMapPath: string;
  resolvedSymbolMapPath: string | null;
  notices: InputNotice[];
}

// ---- Settings (Obfy.App/Models/SettingsSnapshot.cs). Answers `settings.get`; pushed with `settings.changed`. ----

/** The levels the host knows (ObfuscationLevel); `SettingsSnapshot.level` is one of these. */
export type ProtectionLevel = "Minimal" | "Standard" | "Aggressive" | "Custom";

/** The runtimes the host knows (RuntimeProfile); `SettingsSnapshot.runtimeProfile` is one of these. */
export type RuntimeProfileName = "Default" | "NativeAot" | "UnityIl2Cpp" | "BlazorWasm";

/** A change to some settings: `{ "technique.option": value }`, as `settings.update` takes it. Values are bool, number, text or a list of text. */
export type SettingsPatch = Record<string, boolean | number | string | string[] | null>;

/** One technique or output option (TechniqueState). A patch key is `id.optionName`. */
export interface TechniqueState {
  id: string;
  enabled: boolean;
  available: boolean;
  unavailableReason: string | null;
  differsFromLevel: boolean;
  options: Record<string, unknown>;
}

/** What must be fixed before a run (ValidationError); `field` is the patch key at fault. */
export interface ValidationError {
  field: string;
  message: string;
}

/** Pattern lists of an exclusion or inclusion rule set (NameLists). */
export interface NameLists {
  namespaces: string[];
  types: string[];
  methods: string[];
}

/** The values each dropdown offers (SettingsChoices). */
export interface SettingsChoices {
  levels: string[];
  algorithms: string[];
  controlFlowModes: string[];
  namingModes: string[];
  runtimeProfiles: string[];
  packingRids: string[];
}

/** The protection settings with what is available and what blocks a run (SettingsSnapshot). */
export interface SettingsSnapshot {
  revision: number;
  /** Minimal, Standard, Aggressive or Custom. */
  level: string;
  levelSummary: string;
  techniques: TechniqueState[];
  errors: ValidationError[];
  configPath: string | null;
  dirty: boolean;
  /** Default, NativeAot, UnityIl2Cpp or BlazorWasm. */
  runtimeProfile: string;
  exclusions: NameLists;
  inclusions: NameLists;
  choices: SettingsChoices;
  pendingConfigDrop: boolean;
}

// ---- Results (Obfy.App/Models/ResultsSnapshot.cs, StatisticGroup.cs). Answers `results.summary`; pushed with `results.changed`. ----

export type ResultsState = "NotRun" | "Running" | "Finished";

export interface StatisticChip {
  label: string;
  value: number;
}

export interface StatisticGroup {
  name: string;
  chips: StatisticChip[];
}

/** Everything but the symbol tree and the decompiled text (ResultsSnapshot). */
export interface ResultsSnapshot {
  revision: number;
  state: ResultsState;
  groups: StatisticGroup[];
  elapsedMs: number;
  symbolCount: number;
  hasReport: boolean;
  hasPreview: boolean;
  previewPaths: string[];
  selectedPreviewPath: string | null;
  previewError: string | null;
}

/** What a symbol-map entry names (SymbolKind). */
export type SymbolKind = "Namespace" | "Type" | "Method" | "Field" | "Property" | "Parameter" | "Event";

/** One row of the symbol-map tree (SymbolNode). Its children are asked for with `id` as `parentId`. */
export interface SymbolNode {
  /** Stable until the results are reloaded; the same node keeps its id across searches. */
  id: string;
  originalName: string;
  /** Null for a container, which only groups renamed members and is not a rename: show the name once. */
  obfuscatedName: string | null;
  kind: SymbolKind;
  isContainer: boolean;
  /** How many children the node has under the current search. */
  childCount: number;
  /** A search is active and the node has matching children, so the page opens it. */
  expanded: boolean;
}

/** A window of nodes at one level of the tree (SymbolsPage). Answers `results.symbols`. */
export interface SymbolsPage {
  /** The global results revision the page was served from. It rises with every results change, so it says nothing about whether the symbols themselves changed. */
  revision: number;
  parentId: string | null;
  /** How many nodes the level has under the search, not just in this window. */
  total: number;
  /** Index of the first node of `items` within the level. */
  offset: number;
  items: SymbolNode[];
  /** Entries in the whole map, whatever the search. */
  totalSymbols: number;
  hasNoSymbols: boolean;
  hasNoSearchMatches: boolean;
}

/** Answers an export (ExportResult): `exported` is false when the user cancelled the save dialog. */
export interface ExportResult {
  exported: boolean;
  path: string | null;
}

// ---- Log and preview (Obfy.App/Models/LogLine.cs, ResultsSnapshot.cs). ----

export type LogLevel = "Debug" | "Info" | "Warning" | "Error" | "Success";

export interface LogLine {
  id: number;
  /** HH:mm:ss.fff, local time. */
  timestamp: string;
  level: LogLevel;
  message: string;
}

/** The lines added since the last push, pushed with `log.appended` (LogAppended). */
export interface LogAppended {
  /** The log's revision after the last line of the batch. */
  revision: number;
  lines: LogLine[];
}

/** The whole log (LogSnapshot). Answers `log.get` and `log.clear`; pushed with `log.cleared`. */
export interface LogSnapshot {
  revision: number;
  lines: LogLine[];
}

/** The decompiled C# of one output assembly (PreviewResult). Failures are in `error`, never thrown. */
export interface PreviewResult {
  path: string | null;
  text: string;
  error: string | null;
}
