import { strings } from "../../strings";
import type { RunSnapshot } from "../../types/domain";
import "../../components/components.css";

interface RunBarProps {
  /** The host's view of the run; null until it first answers. */
  run: RunSnapshot | null;
  /** The first read failed, so `run` will stay null until the host pushes. */
  unreachable?: boolean;
  fileCount: number;
  includedCount: number;
  /** The protection level's name, once known. */
  level: string | null;
  /** Why the last start or cancel failed. */
  error?: string | null;
  onStart: () => void;
  onCancel: () => void;
}

function percent(run: RunSnapshot): number {
  return Math.round(Math.min(100, Math.max(0, run.progress)));
}

/** What a running run is doing: the file and its place in the run, or the host's own status line. */
function runningText(run: RunSnapshot): string {
  if (run.currentFile && run.total > 0) {
    return `Processing ${run.currentFile} — ${Math.min(run.completed + 1, run.total)} of ${run.total}`;
  }
  return run.message;
}

/** The bar that never leaves: what a run would do, why it cannot start, or how the running one is going. */
export default function RunBar({ run, unreachable, fileCount, includedCount, level, error, onStart, onCancel }: RunBarProps) {
  if (!run) {
    return (
      <div className="runbar" aria-busy={!unreachable}>
        <div className={unreachable ? "sum blocked" : "sum"}>{unreachable ? strings.shell.unreachable : strings.shell.connecting}</div>
        <button type="button" className="btn primary" disabled>
          {strings.run.obfuscate}
        </button>
      </div>
    );
  }

  if (run.status === "Running") {
    return (
      <div className="runbar">
        <div className="prog">
          {run.indeterminate ? (
            <div className="bar indeterminate" role="progressbar" aria-label={strings.run.progress} aria-valuemin={0} aria-valuemax={100}>
              <i />
            </div>
          ) : (
            <div className="bar" role="progressbar" aria-label={strings.run.progress} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent(run)}>
              <i style={{ width: `${percent(run)}%` }} />
            </div>
          )}
          <span aria-live="polite">{runningText(run)}</span>
        </div>
        <button type="button" className="btn" onClick={onCancel}>
          {strings.run.cancel}
        </button>
        {error && <span role="alert" className="sum blocked">{error}</span>}
      </div>
    );
  }

  // A fault is the host's to explain; a block says why Obfuscate is unavailable; otherwise it is the summary.
  const blocked = !run.canStart ? (run.blockedReason ?? run.message) : run.status === "Faulted" ? run.message : null;
  const summary = `${includedCount} of ${fileCount} files · ${level ?? "…"} · Output: ${run.outputHint}`;

  // The reason is read out politely, and its region is always there (empty while there is no reason): a screen reader
  // announces text put into a region it already knows, which a region that arrives together with its text may not be.
  // A failed start has its own alert, and the summary is no live region: it changes with every file and level.
  const reason = error ? null : blocked;
  return (
    <div className="runbar">
      <div role="status" className={reason ? "sum blocked" : "live-region"}>{reason}</div>
      {error ? <div className="sum blocked" role="alert">{error}</div> : !blocked && <div className="sum">{summary}</div>}
      <span className="kbd">Ctrl+Enter</span>
      <button type="button" className="btn primary" data-testid="run-button" disabled={!run.canStart} onClick={onStart}>
        {strings.run.obfuscate}
      </button>
    </div>
  );
}
