import { useEffect, useRef, useState } from "react";
import { apiClient } from "../interop/WebViewApiClient";
import { useWebViewEvent } from "./useWebViewEvent";

/** How long to wait before reading once more after the first read failed (the host may still be starting). */
export const READ_RETRY_DELAY_MS = 1000;

/**
 * State the .NET side owns and pushes: read once on mount with `service.method`, then kept current by `event`.
 * `state` is null until known. A read that fails is tried once more after a short delay; if that fails too,
 * `failed` is true until a push makes the state known. A read that answers null leaves it unknown without failing:
 * the next push will tell.
 */
export function usePushedState<T>(service: string, method: string, event: string): { state: T | null; failed: boolean } {
  const [state, setState] = useState<T | null>(null);
  const [readFailed, setReadFailed] = useState(false);
  // A pushed state is always newer than the one read on mount, even if the read finishes later.
  const pushed = useRef(false);

  useEffect(() => {
    let cancelled = false;
    let retry: ReturnType<typeof setTimeout> | undefined;

    const read = (attempt: number) => {
      apiClient
        .call<T | null>(service, method, {})
        .then((initial) => {
          if (!cancelled && !pushed.current && initial) setState(initial);
        })
        .catch(() => {
          if (cancelled || pushed.current) return;
          if (attempt === 1) {
            retry = setTimeout(() => {
              if (!pushed.current) read(2);
            }, READ_RETRY_DELAY_MS);
          }
          else setReadFailed(true);
        });
    };
    read(1);

    return () => {
      cancelled = true;
      clearTimeout(retry);
    };
  }, [service, method]);

  useWebViewEvent<T>(event, (changed) => {
    pushed.current = true;
    setState(changed);
  });

  return { state, failed: readFailed && state === null };
}
