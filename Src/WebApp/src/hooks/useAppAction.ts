import { useCallback, useEffect, useRef, useState } from "react";
import { failure } from "../interop/errorMessage";

/**
 * A button's call to the host whose failure is shown next to it: `run` clears
 * the last error, runs `action` and answers whether it worked; when it did not, `error` is "<label> (<why>)." until the
 * next run, or until `resetOn` changes: the state the error was about has moved on (the host pushed a newer snapshot,
 * the user went to another screen), so it no longer applies. `run` never rejects, so a click handler can call it with `void`.
 */
export function useAppAction<A extends unknown[]>(
  action: (...args: A) => Promise<void>,
  label: string,
  resetOn?: unknown,
): [run: (...args: A) => Promise<boolean>, error: string | null] {
  const [error, setError] = useState<string | null>(null);

  // Derived while rendering (React's way to adjust state to a value that changed): a new `resetOn` drops the old error.
  const [seen, setSeen] = useState(resetOn);
  if (!Object.is(seen, resetOn)) {
    setSeen(resetOn);
    setError(null);
  }

  // The latest action and label, so that `run` stays the same function while callers pass them inline.
  const latest = useRef({ action, label });
  useEffect(() => {
    latest.current = { action, label };
  });

  const run = useCallback(async (...args: A) => {
    setError(null);
    try {
      await latest.current.action(...args);
      return true;
    } catch (err) {
      setError(failure(latest.current.label, err));
      return false;
    }
  }, []);

  return [run, error];
}
