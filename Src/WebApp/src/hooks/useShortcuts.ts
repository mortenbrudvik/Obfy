import { useEffect, useRef } from "react";

/** What each shortcut does. A shortcut with no handler is left to the browser. */
export interface ShortcutHandlers {
  /** Ctrl+Enter */
  obfuscate?: () => void;
  /** Esc */
  cancel?: () => void;
  /** F1 */
  guide?: () => void;
  /** Ctrl+O */
  addFiles?: () => void;
  /** Ctrl+S */
  saveConfig?: () => void;
  /** Ctrl+L */
  openConfig?: () => void;
}

function handlerFor(event: KeyboardEvent, handlers: ShortcutHandlers): (() => void) | undefined {
  const withCtrl = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey;
  if (withCtrl) {
    switch (event.key.toLowerCase()) {
      case "enter":
        return handlers.obfuscate;
      case "o":
        return handlers.addFiles;
      case "s":
        return handlers.saveConfig;
      case "l":
        return handlers.openConfig;
    }
    return undefined;
  }
  if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return undefined;
  if (event.key === "Escape") return handlers.cancel;
  if (event.key === "F1") return handlers.guide;
  return undefined;
}

/** The window-wide keyboard shortcuts. A handled key does not repeat and is kept from the browser. */
export function useShortcuts(handlers: ShortcutHandlers) {
  const latest = useRef(handlers);
  useEffect(() => {
    latest.current = handlers;
  });

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const handler = handlerFor(event, latest.current);
      if (!handler) return;
      event.preventDefault();
      if (!event.repeat) handler();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);
}
