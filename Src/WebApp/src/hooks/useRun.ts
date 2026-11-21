import { useCallback, useRef } from "react";
import { api, events } from "../interop/api";
import { strings } from "../strings";
import type { RunSnapshot } from "../types/domain";
import { useAppAction } from "./useAppAction";
import { usePushedState } from "./usePushedState";
import { useWebViewEvent } from "./useWebViewEvent";

export interface UseRunOptions {
  /**
   * Called once when a run ends, however it ends, with its final snapshot. `run.completed` is a trigger, not newer
   * state: it carries the same revision as the `run.changed` just before it, so it is never compared by revision and
   * never replaces what `run` holds. The final state reaches `run` through `run.changed`.
   */
  onCompleted?: (final: RunSnapshot) => void;
}

export interface UseRun {
  /** The host's view of the run; null until the first read answers. */
  run: RunSnapshot | null;
  /** The first read failed twice and nothing has been pushed. */
  failed: boolean;
  /** Why the last start or cancel failed, until the next one. */
  error: string | null;
  start: () => Promise<void>;
  cancel: () => Promise<void>;
}

/** The run: read once, kept current by `run.changed`, and started and cancelled through the host. */
export function useRun({ onCompleted }: UseRunOptions = {}): UseRun {
  const { state: run, failed } = usePushedState<RunSnapshot>("run", "get", events.runChanged);

  useWebViewEvent<RunSnapshot>(events.runCompleted, (final) => onCompleted?.(final));

  // A refusal is about the run's state at the time (nothing included, an invalid value): once the host pushes a newer
  // state the refusal is old news, and the run bar goes back to what the state says.
  const [runStart, startError] = useAppAction(async () => {
    await api.run.start();
  }, strings.shell.startFailed, run?.revision);
  // A failed cancel is about a run that is still going, and the host pushes a newer state with every progress report
  // (the revision rises each time): going by the revision, the message would be gone before it could be read. It stays
  // until the run's status changes (it ended) or the user tries again.
  const [runCancel, cancelError] = useAppAction(async () => {
    await api.run.cancel();
  }, strings.shell.cancelFailed, run?.status);

  // A second start while the first is still being asked for would only be refused by the host.
  const starting = useRef(false);
  const start = useCallback(async () => {
    if (starting.current) return;
    starting.current = true;
    try {
      await runStart();
    } finally {
      starting.current = false;
    }
  }, [runStart]);
  const cancel = useCallback(async () => {
    await runCancel();
  }, [runCancel]);

  return { run, failed, error: startError ?? cancelError, start, cancel };
}
