import type { ReactNode } from "react";
import Notice from "../../components/Notice";
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
  const [saveConfig, saveConfigError] = useAppAction(async () => {
    await api.settings.saveConfig();
  }, strings.shell.saveConfigFailed);
  const [openConfig, openConfigError] = useAppAction(async () => {
    await api.settings.openConfig();
  }, strings.shell.openConfigFailed);

  const running = run?.status === "Running";
  useShortcuts({
    obfuscate: run?.canStart ? () => void start() : undefined,
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
    <div className="shell">
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
          onStart={() => void start()}
          onCancel={() => void cancel()}
        />
      </div>
    </div>
  );
}
