import type {
  AppInfo,
  ExportResult,
  InputSnapshot,
  LogSnapshot,
  PreviewResult,
  ResultsSnapshot,
  RunSnapshot,
  SettingsSnapshot,
  SymbolsPage,
} from "../types/domain";
import { apiClient } from "./WebViewApiClient";

/** The events the host pushes, each carrying its service's snapshot (Obfy.App/AppEvents.cs). */
export const events = {
  appInfoChanged: "app.infoChanged",
  settingsChanged: "settings.changed",
  filesChanged: "files.changed",
  logAppended: "log.appended",
  logCleared: "log.cleared",
  resultsChanged: "results.changed",
  runChanged: "run.changed",
  runCompleted: "run.completed",
} as const;

/** A call that waits on the user (a native file dialog): it may take as long as the user does. */
const forUser = { timeoutMs: null } as const;

function call<T>(service: string, method: string, params: object = {}, options?: { timeoutMs?: number | null }): Promise<T> {
  return apiClient.call<T>(service, method, params, options);
}

/**
 * One typed wrapper per host endpoint (Obfy.Interop/ApiRegistrations.cs), each answering the snapshot the host
 * answers with.
 */
export const api = {
  app: {
    info: () => call<AppInfo>("app", "info"),
    ready: () => call<unknown>("app", "ready"),
  },

  settings: {
    get: () => call<SettingsSnapshot>("settings", "get"),
    /** A patch of `{ "technique.option": value }`; see SettingsSnapshot.techniques for the keys. */
    update: (patch: Record<string, unknown>) => call<SettingsSnapshot>("settings", "update", { patch }),
    applyLevel: (level: string) => call<SettingsSnapshot>("settings", "applyLevel", { level }),
    openConfig: () => call<SettingsSnapshot>("settings", "openConfig", {}, forUser),
    saveConfig: (saveAs = false) => call<SettingsSnapshot>("settings", "saveConfig", { saveAs }, forUser),
    pickSigningKey: () => call<SettingsSnapshot>("settings", "pickSigningKey", {}, forUser),
  },

  files: {
    get: () => call<InputSnapshot>("files", "get"),
    pick: () => call<InputSnapshot>("files", "pick", {}, forUser),
    remove: (id: string) => call<InputSnapshot>("files", "remove", { id }),
    clear: () => call<InputSnapshot>("files", "clear"),
    setIncluded: (id: string, included: boolean) => call<InputSnapshot>("files", "setIncluded", { id, included }),
    reveal: (id: string) => call<InputSnapshot>("files", "reveal", { id }),
    openOutput: () => call<InputSnapshot>("files", "openOutput"),
  },

  output: {
    set: (folder: string) => call<InputSnapshot>("output", "set", { folder }),
    pickFolder: () => call<InputSnapshot>("output", "pickFolder", {}, forUser),
    /** Leave a field out to leave it as it is. */
    setSymbolMap: (change: { enabled?: boolean; path?: string }) => call<InputSnapshot>("output", "setSymbolMap", change),
    pickSymbolMap: () => call<InputSnapshot>("output", "pickSymbolMap", {}, forUser),
  },

  log: {
    get: () => call<LogSnapshot>("log", "get"),
    clear: () => call<LogSnapshot>("log", "clear"),
  },

  results: {
    summary: () => call<ResultsSnapshot>("results", "summary"),
    /** One window of the symbol tree: the roots, or the children of `parentId`, filtered by `text`. */
    symbols: (text: string | null, parentId: string | null, offset: number, limit: number) =>
      call<SymbolsPage>("results", "symbols", { text, parentId, offset, limit }),
    exportMap: () => call<ExportResult>("results", "exportMap", {}, forUser),
    exportReport: () => call<ExportResult>("results", "exportReport", {}, forUser),
  },

  preview: {
    get: (path: string | null) => call<PreviewResult>("preview", "get", { path }),
  },

  run: {
    get: () => call<RunSnapshot>("run", "get"),
    /** Answers at once with the running snapshot; throws with the reason when the run cannot start. */
    start: () => call<RunSnapshot>("run", "start"),
    cancel: () => call<RunSnapshot>("run", "cancel"),
  },
};
