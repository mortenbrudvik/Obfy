import { useLayoutEffect, useRef, useState, type DragEvent, type ReactNode } from "react";
import ConfirmDialog from "../../components/ConfirmDialog";
import ErrorBoundary from "../../components/ErrorBoundary";
import Notice from "../../components/Notice";
import { flushPendingEdits } from "../../hooks/pendingEdits";
import { useActionRunner } from "../../hooks/useAppAction";
import { useFileDrag } from "../../hooks/useFileDrag";
import { usePushedState } from "../../hooks/usePushedState";
import { useReadySignal } from "../../hooks/useReadySignal";
import { useRun } from "../../hooks/useRun";
import { useShortcuts } from "../../hooks/useShortcuts";
import { api, events } from "../../interop/api";
import { strings } from "../../strings";
import type { AppInfo, InputNotice, InputSnapshot, ResultsSnapshot, SettingsSnapshot } from "../../types/domain";
import RunBar from "./RunBar";
import ScreenCrash from "./ScreenCrash";
import Sidebar, { type ResultsDot, type Screen } from "./Sidebar";
import "../../components/components.css";
import "./Shell.css";

/** What the host listens for (WebViewHandler.FilesDroppedMessage); the dropped files travel with it. */
const FILES_DROPPED_MESSAGE = "files.dropped";

export interface ShellScreenProps {
  onOpenConfig: () => void;
  notices: InputNotice[];
}

interface ShellProps {
  screen: Screen;
  onNavigate: (screen: Screen) => void;
  /** The current screen, or a render function receiving shell-level actions and notices. */
  children: ReactNode | ((props: ShellScreenProps) => ReactNode);
}

/**
 * The window's frame: the sidebar, the current screen, the run bar that is always there, and the keyboard shortcuts.
 * It reads the little each of those needs (file count, level, results state, run) from the host.
 */
export default function Shell({ screen, onNavigate, children }: ShellProps) {
  // The version never changes while the app runs, so nothing is pushed for it.
  const { state: info } = usePushedState<AppInfo>("app", "info", null);
  const { state: files, failed: filesFailed } = usePushedState<InputSnapshot>("files", "get", events.filesChanged);
  const { state: settings, failed: settingsFailed } = usePushedState<SettingsSnapshot>("settings", "get", events.settingsChanged);
  const [answeredSettings, setAnsweredSettings] = useState<{ next: SettingsSnapshot; over: SettingsSnapshot | null } | null>(null);
  const latestSettings = useRef(settings);
  useLayoutEffect(() => {
    latestSettings.current = settings;
  });
  const currentSettings =
    answeredSettings &&
    answeredSettings.over === settings &&
    (!settings || answeredSettings.next.revision >= settings.revision)
      ? answeredSettings.next
      : settings;
  const { state: results, failed: resultsFailed } = usePushedState<ResultsSnapshot>("results", "summary", events.resultsChanged);

  const { run, failed, error: runError, start, cancel } = useRun({
    // The run's end is a cue, not state: a clean run opens its results.
    onCompleted: (final) => {
      if (final.status === "Done" && final.failed === 0) onNavigate("results");
    },
  });

  // The host shows its window once the page has something to draw: each snapshot is in, or its read gave up.
  useReadySignal(
    (run !== null || failed) && (files !== null || filesFailed) && (settings !== null || settingsFailed) && (results !== null || resultsFailed),
  );

  const running = run?.status === "Running";
  const pendingConfigDrop = Boolean(currentSettings?.pendingConfigDrop);

  // A shortcut's failure is shown on whatever screen is open, so it goes when the user moves on, the state changes,
  // or another shell action runs.
  const staleAfter = [screen, files?.revision, currentSettings?.revision].join("|");
  const [runAction, shortcutError] = useActionRunner(staleAfter);
  const [runConfigDropAction, configDropError] = useActionRunner(
    [pendingConfigDrop, currentSettings?.revision].join("|"),
  );

  const addFiles = () =>
    runAction(strings.shell.addFilesFailed, async () => {
      await api.files.pick();
    });
  // Text typed into a box is committed when the box is left, which a shortcut does not do: it is sent first, and the
  // host has it before it is asked to save or to run.
  const saveConfig = (saveAs: boolean) =>
    runAction(strings.shell.saveConfigFailed, async () => {
      if ((await flushPendingEdits()) === "failed") throw new Error(strings.protection.failed.pending);
      await api.settings.saveConfig(saveAs);
    });
  const openConfig = () =>
    runAction(strings.shell.openConfigFailed, async () => {
      await api.settings.openConfig();
    });
  const confirmConfigDrop = () =>
    runConfigDropAction(strings.shell.openConfigFailed, async () => {
      const over = latestSettings.current;
      const next = await api.settings.confirmConfigDrop();
      setAnsweredSettings({ next, over });
    });
  const cancelConfigDrop = () =>
    runConfigDropAction(strings.shell.cancelConfigDropFailed, async () => {
      const over = latestSettings.current;
      const next = await api.settings.cancelConfigDrop();
      setAnsweredSettings({ next, over });
    });
  const [confirmOpenConfig, setConfirmOpenConfig] = useState(false);

  // The web area takes the OS drop, so the page receives it and hands the files to the host, which reads their paths and
  // applies the rules (a run refuses them with a notice). The overlay is shown while a drag of files is over the window.
  const { dragging, onDragEnter, onDragOver, onDragLeave, end: endDrag } = useFileDrag();
  const onDrop = (event: DragEvent) => {
    event.preventDefault();
    endDrag();
    const dropped = Array.from(event.dataTransfer.files);
    if (dropped.length > 0) window.chrome?.webview?.postMessageWithAdditionalObjects(FILES_DROPPED_MESSAGE, dropped);
  };

  // A run is blocked until a typed value (a watermark id) reaches the host, so the block is judged after it was sent.
  // In a layout effect, like the shortcut handlers: a Ctrl+Enter right after the run state arrives must judge that state.
  const latestRun = useRef(run);
  useLayoutEffect(() => {
    latestRun.current = run;
  });
  const startAfterPendingEdits = async () => {
    const flushed = await flushPendingEdits();
    if (flushed === "failed") return;
    if (flushed === "sent" || latestRun.current?.canStart) await start();
  };

  const requestOpenConfig = () => {
    if (running || pendingConfigDrop) return;
    if (currentSettings?.dirty) {
      setConfirmOpenConfig(true);
    } else {
      void openConfig();
    }
  };

  useShortcuts({
    obfuscate: running ? undefined : () => void startAfterPendingEdits(),
    cancel: running ? () => void cancel() : undefined,
    guide: () => onNavigate("guide"),
    addFiles: running ? undefined : () => void addFiles(),
    saveConfig: () => void saveConfig(false),
    saveConfigAs: () => void saveConfig(true),
    openConfig: running ? undefined : requestOpenConfig,
  });

  // Trying a broken screen again removes the button that had focus: focus goes to the content area, where the screen is.
  const contentRef = useRef<HTMLElement>(null);

  const fileCount = files?.files.length ?? 0;
  const includedCount = files?.files.filter((file) => file.included).length ?? 0;
  const level = currentSettings?.level ?? null;
  const resultsDot: ResultsDot = running ? "running" : results?.state === "Finished" ? "done" : "none";
  const configNotices =
    files?.notices?.filter(
      (n) =>
        n.title === "Unknown configuration keys" ||
        n.title === "Could not load configuration" ||
        n.title === "Not an Obfy configuration" ||
        n.message.startsWith("Unknown configuration keys ignored:"),
    ) ?? [];

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
        <main className="content" ref={contentRef} tabIndex={-1}>
          {shortcutError && <Notice tone="error">{shortcutError}</Notice>}
          {/* A screen that throws is replaced by its own message here, so the sidebar, the run bar and the shortcuts stay.
              Keyed on the screen: moving to another one starts it afresh. */}
          <ErrorBoundary
            key={screen}
            screen={screen}
            fallback={(retry) => (
              <ScreenCrash
                screen={screen}
                onRetry={() => {
                  retry();
                  contentRef.current?.focus();
                }}
              />
            )}
          >
            {typeof children === "function" ? children({ onOpenConfig: requestOpenConfig, notices: configNotices }) : children}
          </ErrorBoundary>
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
      <ConfirmDialog
        open={pendingConfigDrop || confirmOpenConfig}
        title={pendingConfigDrop ? strings.protection.confirmDropConfig.title : strings.protection.confirmOpenConfig.title}
        message={pendingConfigDrop ? strings.protection.confirmDropConfig.body : strings.protection.confirmOpenConfig.body}
        confirmLabel={
          pendingConfigDrop ? strings.protection.confirmDropConfig.confirm : strings.protection.confirmOpenConfig.confirm
        }
        cancelLabel={
          pendingConfigDrop ? strings.protection.confirmDropConfig.cancel : strings.protection.confirmOpenConfig.cancel
        }
        confirmDisabled={pendingConfigDrop && running}
        error={pendingConfigDrop ? configDropError : null}
        onCancel={() => {
          if (pendingConfigDrop) {
            setConfirmOpenConfig(false);
            void cancelConfigDrop();
          } else {
            setConfirmOpenConfig(false);
          }
        }}
        onConfirm={() => {
          if (pendingConfigDrop) {
            if (running) return;
            setConfirmOpenConfig(false);
            void confirmConfigDrop();
          } else {
            setConfirmOpenConfig(false);
            if (!running) void openConfig();
          }
        }}
      />
    </div>
  );
}
