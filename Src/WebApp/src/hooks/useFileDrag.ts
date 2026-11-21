import { useCallback, useEffect, useRef, useState, type DragEvent } from "react";

/**
 * How long a file drag may go without a `dragover` before it is taken to be over. A drag over the page fires `dragover`
 * every 350 ms or so, even with the mouse still, so a silence this long means it left (or was cancelled) without the
 * page being told.
 */
export const DRAG_IDLE_MS = 1500;

/** Whether a drag carries files (not a link or selected text), judged from its types, which a drag exposes before the drop. */
const carriesFiles = (event: DragEvent) => Array.from(event.dataTransfer?.types ?? []).includes("Files");

export interface FileDrag {
  /** Files are being dragged over the page. */
  dragging: boolean;
  onDragEnter: (event: DragEvent) => void;
  onDragOver: (event: DragEvent) => void;
  onDragLeave: (event: DragEvent) => void;
  /** The drag is over (the drop happened): hide the overlay and start counting again. */
  end: () => void;
}

/**
 * Tracks a drag of files over the window, for the overlay. Child elements make dragenter/dragleave fire in pairs, so a
 * count of enters over leaves says whether the drag is still inside. The count alone can stick, though: no dragleave
 * comes when the element under the cursor is removed mid-drag, or when the drag is cancelled outside the window. So the
 * count is reset when `dragover` stops arriving, when a drag ends and when the window loses focus, and the next
 * `dragover` of a drag that is still there shows the overlay again.
 */
export function useFileDrag(): FileDrag {
  const [dragging, setDragging] = useState(false);
  const depth = useRef(0);
  const idle = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const end = useCallback(() => {
    clearTimeout(idle.current);
    depth.current = 0;
    setDragging(false);
  }, []);

  const keepAlive = useCallback(() => {
    clearTimeout(idle.current);
    idle.current = setTimeout(end, DRAG_IDLE_MS);
  }, [end]);

  useEffect(() => {
    window.addEventListener("dragend", end);
    window.addEventListener("blur", end);
    return () => {
      window.removeEventListener("dragend", end);
      window.removeEventListener("blur", end);
      clearTimeout(idle.current);
    };
  }, [end]);

  const onDragEnter = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    event.preventDefault();
    depth.current += 1;
    setDragging(true);
    keepAlive();
  };
  const onDragOver = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    // Cancelling dragover is what lets the element take a drop; without it the browser would open the file.
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    setDragging(true);
    keepAlive();
  };
  const onDragLeave = (event: DragEvent) => {
    if (!carriesFiles(event)) return;
    depth.current = Math.max(0, depth.current - 1);
    if (depth.current === 0) setDragging(false);
  };

  return { dragging, onDragEnter, onDragOver, onDragLeave, end };
}
