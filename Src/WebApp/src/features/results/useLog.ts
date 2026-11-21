import { useCallback, useEffect, useRef, useState } from "react";
import { useAppAction } from "../../hooks/useAppAction";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import { api, events } from "../../interop/api";
import { strings } from "../../strings";
import type { LogAppended, LogLine, LogSnapshot } from "../../types/domain";
import { appendBatch, emptyLog, replaceLog, type Held } from "./logModel";

export interface UseLog {
  /** The latest lines the page keeps (see `MAX_LOG_LINES`), oldest first. */
  lines: LogLine[];
  /** How many older lines the page let go of to stay within that bound. */
  trimmed: number;
  /** Why the last clear failed, until the next one. */
  error: string | null;
  clear: () => Promise<void>;
}

/**
 * The run log: read once with `log.get`, then kept current by `log.appended` (batches of new lines) and `log.cleared`.
 * Pushes can overtake the first read and reach the page out of order, so each carries the revision it reflects and
 * each line an id: what the page already holds is dropped, and a batch that arrives after a newer one is merged by id.
 * Pushes that arrive before the read answers are kept and applied on top of it. The page keeps a bounded tail (`logModel`).
 */
export function useLog(): UseLog {
  const [held, setHeld] = useState<Held>(emptyLog);
  const loaded = useRef(false);
  const early = useRef<((held: Held) => Held)[]>([]);

  const apply = useCallback((change: (held: Held) => Held) => {
    if (loaded.current) setHeld(change);
    else early.current.push(change);
  }, []);

  useEffect(() => {
    let cancelled = false;
    const done = (initial: LogSnapshot | null) => {
      if (cancelled) return;
      loaded.current = true;
      const pending = early.current;
      early.current = [];
      setHeld((current) => pending.reduce((acc, change) => change(acc), initial ? replaceLog(current, initial) : current));
    };
    api.log.get().then(done, () => done(null));
    return () => {
      cancelled = true;
    };
  }, []);

  useWebViewEvent<LogAppended>(events.logAppended, (batch) => apply((current) => appendBatch(current, batch)));
  useWebViewEvent<LogSnapshot>(events.logCleared, (snapshot) => apply((current) => replaceLog(current, snapshot)));

  // A failed clear is shown, not left as an unhandled rejection.
  const [runClear, error] = useAppAction(async () => {
    const snapshot = await api.log.clear();
    apply((current) => replaceLog(current, snapshot));
  }, strings.results.logView.clearFailed);
  const clear = useCallback(async () => {
    await runClear();
  }, [runClear]);

  return { lines: held.lines, trimmed: held.trimmed, error, clear };
}
