import { useState } from "react";

/** A text box the user types in and the host also changes: it follows the host's value, and the page commits when the box is left. */
export function useDraft(value: string): [string, (draft: string) => void] {
  const [draft, setDraft] = useState(value);
  const [seen, setSeen] = useState(value);
  if (seen !== value) {
    setSeen(value);
    setDraft(value);
  }
  return [draft, setDraft];
}
