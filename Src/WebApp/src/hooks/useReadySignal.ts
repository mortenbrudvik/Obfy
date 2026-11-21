import { useEffect, useRef } from "react";
import { api } from "../interop/api";

/** Runs `callback` after the browser has painted what React just committed (the second frame is the one after the paint). */
function afterNextPaint(callback: () => void): () => void {
  if (typeof requestAnimationFrame !== "function") {
    const timer = setTimeout(callback, 0);
    return () => clearTimeout(timer);
  }
  let second = 0;
  const first = requestAnimationFrame(() => {
    second = requestAnimationFrame(callback);
  });
  return () => {
    cancelAnimationFrame(first);
    if (second) cancelAnimationFrame(second);
  };
}

/**
 * Tells the host (`app.ready`) that the page has drawn its first real content, once. The host keeps its window hidden
 * until it hears this (or its own timeout passes), so the call waits for `settled`: the first snapshots are in, or
 * their reads have given up and the page shows why. A refused call is ignored: the host reveals the window on its own.
 */
export function useReadySignal(settled: boolean) {
  const sent = useRef(false);

  useEffect(() => {
    if (!settled || sent.current) return;
    return afterNextPaint(() => {
      if (sent.current) return;
      sent.current = true;
      api.app.ready().catch(() => {});
    });
  }, [settled]);
}
