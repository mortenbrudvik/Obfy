import { useEffect, useRef, useState, type DragEvent, type ReactNode } from "react";
import Notice from "../../components/Notice";
import { flushPendingEdits } from "../../hooks/pendingEdits";
import { useAppAction } from "../../hooks/useAppAction";
import { usePushedState } from "../../hooks/usePushedState";
import { useRun } from "../../hooks/useRun";
import { useShortcuts } from "../../hooks/useShortcuts";
import { api, events } from "../../interop/api";
import { strings } from "../../strings";
import type { AppInfo, InputSnapshot, ResultsSnapshot, SettingsSnapshot } from "../../types/domain";
import RunBar from "./RunBar";
import Sidebar, { type ResultsDot, type Screen } from "./Sidebar";
import "../../components/components.css";
import "./Shell.css";

/** What the host listens for (WebViewHandler.FilesDroppedMessage); the dropped files travel with it. */
const FILES_DROPPED_MESSAGE = "files.dropped";

/** Whether a drag carries files (not a link or selected text), judged from its types, which a drag exposes before the drop. */
const carriesFiles = (event: DragEvent) => Array.from(event.dataTransfer?.types ?? []).includes("Files");

interface ShellProps {
  screen: Screen;
  onNavigate: (screen: Screen) => void;
  /** The current screen. */
  children: ReactNode;
}

/**
 * The window's frame: the sidebar, the current screen, the run bar that is always there, and the keyboard shortcuts.
 * It reads the little each of those needs (file count, level, results state, run) from the host.
 */
export default function Shell({ screen, onNavigate, children }: ShellProps) {
  const { state: info } = usePushedState<AppInfo>("app", "info", events.appInfoChanged);
  const { state: files } = usePushedState<InputSnapshot>("files", "get", events.filesChanged);
  const { state: settings } = usePushedState<SettingsSnapshot>("settings", "get", events.settingsChanged);
  const { state: results } = usePushedState<ResultsSnapshot>("results", "summary", events.resultsChanged);

  const { run, failed, error: runError, start, cancel } = useRun({
    // The run's end is a cue, not state: a clean run opens its results.
    onCompleted: (final) => {
      if (final.status === "Done" && final.failed === 0) onNavigate("results");
    },
  });

  const [addFiles, addFilesError] = useAppAction(async () => {
    await api.files.pick();
  }, strings.shell.addFilesFailed);
  // Text typed into a box is committed when the box is left, which a shortcut does not do: it is sent first, and the
  // host has it before it is asked to save or to run.
  const [saveConfig, saveConfigError] = useAppAction(async () => {
    if ((await flushPendingEdits()) === "failed") throw new Error(strings.protection.failed.pending);
    await api.settings.saveConfig();
  }, strings.shell.saveConfigFailed);
  const [openConfig, openConfigError] = useAppAction(async () => {
    await api.settings.openConfig();
  }, strings.shell.openConfigFailed);

  const running = run?.status === "Running";

  // The web area takes the OS drop, so the page receives it and hands the files to the host, which reads their paths and
  // applies the rules (a run refuses them with a notice). Child elements make dragenter/dragleave fire in pairs, so the
  // overlay is shown while more enters than leaves have been seen.
  const [dragDepth, setDragDepth] = useState(0);
  const dragging = dragDepth > 0;
  const onDragEnter = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    setDragDepth((depth) => depth + 1);
  };
  const onDragOver = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    // Cancelling dragover is what lets the element take a drop; without it the browser would open the file.
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
  };
  const onDragLeave = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    setDragDepth((depth) => Math.max(0, depth - 1));
  };
  const onDrop = (event: DragEvent) => {
    event.preventDefault();
    setDragDepth(0);
    const dropped = Array.from(event.dataTransfer.files);
    if (dropped.length > 0) window.chrome?.webview?.postMessageWithAdditionalObjects(FILES_DROPPED_MESSAGE, dropped);
  };

  // A run is blocked until a typed value (a watermark id) reaches the host, so the block is judged after it was sent.
  const latestRun = useRef(run);
  useEffect(() => {
    latestRun.current = run;
  });
  const startAfterPendingEdits = async () => {
    const flushed = await flushPendingEdits();
    if (flushed === "failed") return;
    if (flushed === "sent" || latestRun.current?.canStart) await start();
  };

  useShortcuts({
    obfuscate: running ? undefined : () => void startAfterPendingEdits(),
    cancel: running ? () => void cancel() : undefined,
    guide: () => onNavigate("guide"),
    addFiles: () => void addFiles(),
    saveConfig: () => void saveConfig(),
    openConfig: () => void openConfig(),
  });

  const fileCount = files?.files.length ?? 0;
  const includedCount = files?.files.filter((file) => file.included).length ?? 0;
  const level = settings?.level ?? null;
  const resultsDot: ResultsDot = running ? "running" : results?.state === "Finished" ? "done" : "none";
  const shortcutError = addFilesError ?? saveConfigError ?? openConfigError;

  return (
    <div className="shell" onDragEnter={onDragEnter} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}>
      {dragging && (
        <div className={`drop-overlay${running ? " blocked" : ""}`} data-testid="drop-overlay" aria-hidden="true">
          <span>{running ? strings.shell.dropOverlayRunning : strings.shell.dropOverlay}</span>
        </div>
      )}
      <Sidebar
        screen={screen}
        onNavigate={onNavigate}
        includedCount={includedCount}
        fileCount={fileCount}
        level={level}
        results={resultsDot}
        version={info?.version ?? null}
      />
      <div className="main">
        <main className="content">
          {shortcutError && <Notice tone="error">{shortcutError}</Notice>}
          {children}
        </main>
        <RunBar
          run={run}
          unreachable={failed}
          fileCount={fileCount}
          includedCount={includedCount}
          level={level}
          error={runError}
          onStart={() => void startAfterPendingEdits()}
          onCancel={() => void cancel()}
        />
      </div>
    </div>
  );
}
