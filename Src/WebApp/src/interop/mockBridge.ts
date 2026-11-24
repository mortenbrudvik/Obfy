import type { WebView } from "../types/WebView";
import type { AppInfo, InputSnapshot, ResultsSnapshot, RunSnapshot, SettingsSnapshot } from "../types/domain";

/** Answers one call; it gets the parsed params and returns the call's data. */
export type MockHandler = (params: unknown) => unknown | Promise<unknown>;

/** Handlers by `"service.method"`. */
export type MockHandlers = Record<string, MockHandler>;

const defaultSettings = (): SettingsSnapshot => ({
  revision: 1, level: "Standard", levelSummary: "", techniques: [], errors: [], configPath: null, dirty: false,
  runtimeProfile: "Default", exclusions: { namespaces: [], types: [], methods: [] },
  inclusions: { namespaces: [], types: [], methods: [] },
  choices: { levels: [], algorithms: [], controlFlowModes: [], namingModes: [], runtimeProfiles: [], packingRids: [] },
  pendingConfigDrop: false,
});

/** A host with nothing added yet, for the shell in a plain browser: Obfuscate is blocked and nothing has run. */
export const defaultMockHandlers: MockHandlers = {
  "app.info": (): AppInfo => ({ version: "0.0.0-mock" }),
  "app.ready": () => ({}),
  // A plain browser has no host to open a browser window for the guide.
  "app.openGuide": () => ({}),
  "app.reportError": () => ({}),
  "run.get": (): RunSnapshot => ({
    revision: 1, canStart: false, blockedReason: "Add at least one included file.", mode: "None", modeExplanation: "",
    outputHint: "", progress: 0, indeterminate: false, currentFile: null, completed: 0, total: 0, failed: 0,
    status: "Idle", message: "",
  }),
  "files.get": (): InputSnapshot => ({
    revision: 1, files: [], hasIncludedFiles: false, outputFolder: "", outputHint: "", mergeOutputHint: "",
    outputWarning: null, writeSymbolMap: false, symbolMapPath: "", resolvedSymbolMapPath: null, notices: [],
  }),
  "settings.get": defaultSettings,
  "settings.confirmConfigDrop": defaultSettings,
  "settings.cancelConfigDrop": defaultSettings,
  "results.summary": (): ResultsSnapshot => ({
    revision: 1, state: "NotRun", groups: [], elapsedMs: 0, symbolCount: 0, hasReport: false, hasPreview: false,
    previewPaths: [], selectedPreviewPath: null, previewError: null,
  }),
};

/** An in-memory host: a WebView that answers in the host's envelope. */
export interface MockWebView extends WebView {
  /** Pushes an event to the page, as the host's ApiEventAggregator does. */
  emit(eventName: string, data: unknown): void;
}

/** A WebView backed by `handlers`, for a plain browser (`npm run preview`) and tests. */
export function createMockWebView(handlers: MockHandlers = defaultMockHandlers): MockWebView {
  const listeners: ((event: MessageEvent<unknown>) => void)[] = [];

  return {
    addEventListener(_type, listener) {
      listeners.push(listener);
    },

    // A plain browser has no host to hand dropped files to.
    postMessageWithAdditionalObjects() {},

    hostObjects: {
      apibridge: {
        async InvokeMethod(service, method, jsonParams) {
          const name = `${service}.${method}`;
          const handler = Object.hasOwn(handlers, name) ? handlers[name] : undefined;
          if (!handler) return failure("UnknownApi", `Unknown API: ${name}`);
          try {
            const data = await handler(JSON.parse(jsonParams));
            return JSON.stringify({ success: true, data: data === undefined ? {} : data });
          } catch (err) {
            return failure("Internal", err instanceof Error ? err.message : String(err));
          }
        },
      },
    },

    emit(eventName, data) {
      // Through JSON, as the host's messages are.
      const message: unknown = JSON.parse(JSON.stringify({ eventName, data }));
      listeners.forEach((listener) => listener(new MessageEvent("message", { data: message })));
    },
  };
}

function failure(code: string, message: string): string {
  return JSON.stringify({ success: false, error: { code, message } });
}

/** Installs the mock on `window.chrome.webview` when the real WebView2 bridge is absent. Returns whether it did. */
export function installMockBridgeIfAbsent(handlers?: MockHandlers): boolean {
  if (window.chrome?.webview) return false;
  window.chrome = { ...window.chrome, webview: createMockWebView(handlers) };
  return true;
}
