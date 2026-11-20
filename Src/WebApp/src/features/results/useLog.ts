import { useCallback, useEffect, useRef, useState } from "react";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import { api, events } from "../../interop/api";
import type { LogAppended, LogLine, LogSnapshot } from "../../types/domain";

export interface UseLog {
  lines: LogLine[];
  clear: () => Promise<void>;
}

interface Held {
  revision: number;
  lines: LogLine[];
}

/** Adds the lines of a batch that the log does not hold yet. A batch not newer than the log is stale and changes nothing. */
function append(held: Held, batch: LogAppended): Held {
  if (batch.revision <= held.revision) return held;
  const lastId = held.lines.length > 0 ? held.lines[held.lines.length - 1].id : 0;
  const fresh = batch.lines.filter((line) => line.id > lastId);
  return { revision: batch.revision, lines: fresh.length > 0 ? [...held.lines, ...fresh] : held.lines };
}

/** Takes a whole log in place of the held one, unless the held one is newer. */
function replace(held: Held, snapshot: LogSnapshot): Held {
  return snapshot.revision >= held.revision ? { revision: snapshot.revision, lines: snapshot.lines } : held;
}

/**
 * The run log: read once with `log.get`, then kept current by `log.appended` (batches of new lines) and `log.cleared`.
 * Pushes can overtake the first read and reach the page out of order, so each carries the revision it reflects and
 * each line an id: what the page already holds is dropped. Pushes that arrive before the read answers are kept and
 * applied on top of it.
 */
export function useLog(): UseLog {
  const [held, setHeld] = useState<Held>({ revision: 0, lines: [] });
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
      setHeld((current) => pending.reduce((acc, change) => change(acc), initial ? replace(current, initial) : current));
    };
    api.log.get().then(done, () => done(null));
    return () => {
      cancelled = true;
    };
  }, []);

  useWebViewEvent<LogAppended>(events.logAppended, (batch) => apply((current) => append(current, batch)));
  useWebViewEvent<LogSnapshot>(events.logCleared, (snapshot) => apply((current) => replace(current, snapshot)));

  const clear = useCallback(async () => {
    const snapshot = await api.log.clear();
    apply((current) => replace(current, snapshot));
  }, [apply]);

  return { lines: held.lines, clear };
}
