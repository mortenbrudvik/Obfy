import { useCallback, useLayoutEffect, useRef, useState } from "react";
import { failure } from "../interop/errorMessage";

/**
 * Shared runner for host calls that share one error slot: running any action clears the previous error, and a late
 * rejection after `resetOn` changed or after a newer action started does not overwrite state with a stale error.
 */
export function useActionRunner(
  resetOn?: unknown,
): [run: (label: string, action: () => Promise<void>) => Promise<boolean>, error: string | null] {
  const [error, setError] = useState<string | null>(null);

  // Derived while rendering (React's way to adjust state to a value that changed): a new `resetOn` drops the old error.
  const [seen, setSeen] = useState(resetOn);
  if (!Object.is(seen, resetOn)) {
    setSeen(resetOn);
    setError(null);
  }

  const resetVersion = useRef(0);
  const lastResetOn = useRef(resetOn);
  useLayoutEffect(() => {
    if (!Object.is(lastResetOn.current, resetOn)) {
      lastResetOn.current = resetOn;
      resetVersion.current++;
    }
  }, [resetOn]);

  const callSeq = useRef(0);

  const run = useCallback(async (label: string, action: () => Promise<void>) => {
    const startResetVersion = resetVersion.current;
    const seq = ++callSeq.current;
    setError(null);
    try {
      await action();
      return true;
    } catch (err) {
      if (resetVersion.current === startResetVersion && callSeq.current === seq) {
        setError(failure(label, err));
      }
      return false;
    }
  }, []);

  return [run, error];
}

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
  const [runAction, error] = useActionRunner(resetOn);

  // The latest action and label, so that `run` stays the same function while callers pass them inline.
  const latest = useRef({ action, label });
  useLayoutEffect(() => {
    latest.current = { action, label };
  });

  const run = useCallback(
    (...args: A) => runAction(latest.current.label, () => latest.current.action(...args)),
    [runAction],
  );

  return [run, error];
}
