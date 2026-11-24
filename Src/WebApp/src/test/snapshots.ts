import type { InputSnapshot, ResultsSnapshot, RunSnapshot, SettingsSnapshot } from "../types/domain";

/** Snapshots as the host sends them, for tests: spread one and override what the test is about. */

export const idleRun: RunSnapshot = {
  revision: 1,
  canStart: false,
  blockedReason: "Add at least one included file.",
  mode: "None",
  modeExplanation: "",
  outputHint: "",
  progress: 0,
  indeterminate: false,
  currentFile: null,
  completed: 0,
  total: 0,
  failed: 0,
  status: "Idle",
  message: "",
};

export const readyRun: RunSnapshot = {
  ...idleRun,
  revision: 2,
  canStart: true,
  blockedReason: null,
  mode: "Single",
  outputHint: "App.obfuscated.dll next to App.dll",
};

export const emptyFiles: InputSnapshot = {
  revision: 1,
  files: [],
  hasIncludedFiles: false,
  outputFolder: "",
  outputHint: "",
  mergeOutputHint: "",
  outputWarning: null,
  writeSymbolMap: false,
  symbolMapPath: "",
  resolvedSymbolMapPath: null,
  notices: [],
};

export const standardSettings: SettingsSnapshot = {
  revision: 1,
  level: "Standard",
  levelSummary: "",
  techniques: [],
  errors: [],
  configPath: null,
  dirty: false,
  runtimeProfile: "Default",
  exclusions: { namespaces: [], types: [], methods: [] },
  inclusions: { namespaces: [], types: [], methods: [] },
  choices: { levels: [], algorithms: [], controlFlowModes: [], namingModes: [], runtimeProfiles: [], packingRids: [] },
  pendingConfigDrop: false,
};

export const notRunResults: ResultsSnapshot = {
  revision: 1,
  state: "NotRun",
  groups: [],
  elapsedMs: 0,
  symbolCount: 0,
  hasReport: false,
  hasPreview: false,
  previewPaths: [],
  selectedPreviewPath: null,
  previewError: null,
};
