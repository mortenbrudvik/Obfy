import { useEffect, useRef } from "react";

/** What committing a box came to: it held nothing new, its text reached the host, or the host did not take it in. */
export type FlushResult = "idle" | "sent" | "failed";

/** Sends what the user has typed into a box and not yet committed (a box commits when it is left). */
export type FlushEdit = () => Promise<FlushResult>;

const pending = new Set<FlushEdit>();

/**
 * Commits every box that holds text the host has not seen, and waits until the host has answered. Anything that acts on
 * the settings without the box being left first (a window-wide shortcut: run, save) calls this before it asks the host,
 * and goes ahead only unless the answer is `failed`: acting on an old value could stamp the wrong id into the output.
 * Answers `sent` when anything was sent, `idle` when nothing was waiting.
 */
export async function flushPendingEdits(): Promise<FlushResult> {
  const results = await Promise.all([...pending].map((flush) => flush()));
  if (results.includes("failed")) return "failed";
  return results.includes("sent") ? "sent" : "idle";
}

/** Makes `flush` known to `flushPendingEdits` while the calling component is on the page. */
export function usePendingEdit(flush: FlushEdit) {
  const latest = useRef(flush);
  useEffect(() => {
    latest.current = flush;
  });

  useEffect(() => {
    const entry: FlushEdit = () => latest.current();
    pending.add(entry);
    return () => {
      pending.delete(entry);
    };
  }, []);
}
