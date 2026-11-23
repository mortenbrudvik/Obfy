import { useEffect, useId, useLayoutEffect, useRef, type MouseEvent } from "react";
import "./components.css";

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/** An in-app modal confirmation dialog with Escape-to-cancel, Tab trapping and focus restoration. */
export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  cancelLabel = "Cancel",
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const titleId = useId();
  const bodyId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const latestCancel = useRef(onCancel);

  useLayoutEffect(() => {
    latestCancel.current = onCancel;
  });

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    cancelRef.current?.focus();

    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        latestCancel.current();
        return;
      }

      if (event.key !== "Tab") return;

      const focusables = [cancelRef.current, confirmRef.current].filter(
        (button): button is HTMLButtonElement => button !== null && !button.disabled,
      );
      if (focusables.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      const active = document.activeElement;
      const inDialog = dialogRef.current?.contains(active) ?? false;
      const inFocusables = focusables.includes(active as HTMLButtonElement);

      if (!inDialog || !inFocusables) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
      } else if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => {
      window.removeEventListener("keydown", onKeyDown, true);
      if (previous?.isConnected) previous.focus();
    };
  }, [open]);

  if (!open) return null;

  const onBackdropMouseDown = (event: MouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) onCancel();
  };

  return (
    <div className="dialog-backdrop" onMouseDown={onBackdropMouseDown}>
      <div
        ref={dialogRef}
        className="dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
      >
        <h2 id={titleId}>{title}</h2>
        <p id={bodyId}>{message}</p>
        <div className="dialog-actions">
          <button ref={cancelRef} type="button" className="btn" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button ref={confirmRef} type="button" className="btn primary" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
