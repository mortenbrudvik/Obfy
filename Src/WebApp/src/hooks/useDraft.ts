import { useCallback, useReducer } from "react";

interface DraftState {
  /** What the box shows. */
  draft: string;
  /** What the draft is measured against: when it is the same as the draft, the box holds nothing the host has not been given. */
  base: string;
  /** The host's value as of the last render. */
  seen: string;
}

type DraftAction =
  | { type: "host"; value: string }
  | { type: "edit"; text: string }
  | { type: "sent"; text: string }
  | { type: "answered"; text: string; accepted: boolean };

function reduce(state: DraftState, action: DraftAction): DraftState {
  switch (action.type) {
    // The host's value changed. The box follows it unless it holds text the host has not seen: typing never gets
    // overwritten, not even by the echo of the commit before it.
    case "host":
      return { seen: action.value, base: action.value, draft: state.draft === state.base ? action.value : state.draft };
    case "edit":
      return { ...state, draft: action.text };
    // From the moment a text is sent it is what the host is about to hold, so only typing after that is a new edit.
    case "sent":
      return { ...state, base: action.text };
    // The host has answered. Whatever it did with the text (it kept the value it had, or it changed it) is its value now,
    // and the box shows that unless the user typed since. A refused call leaves the box alone: the text stays an edit
    // the host has not seen, to be sent again or fixed, not silently dropped.
    case "answered":
      return {
        ...state,
        base: state.seen,
        draft: action.accepted && state.draft === action.text ? state.seen : state.draft,
      };
  }
}

/** What a commit answers: `false` is a refusal; anything else, or nothing, means the host took the call in. */
export type Sent = void | boolean | Promise<void | boolean>;

/** The text of a box and the way to send it: `send(text, call)` makes `call(text)` and answers what it answered. */
export type Draft = [draft: string, setDraft: (draft: string) => void, send: (text: string, call: (text: string) => Sent) => Promise<void | boolean>];

/**
 * A text box the user types in and the host also changes: it follows the host's value, the page commits what was typed
 * when the box is left, and what the user types after a commit is not lost to the host's echo of it.
 *
 * Commit through `send`, so the box knows which text the host is about to answer and when it has: a value the host
 * answers with in place of what was sent (it normalised it, or kept the one it had) is shown once nothing was typed
 * since, and a call that is refused (answers `false`, or throws) leaves what was typed in the box.
 */
export function useDraft(value: string): Draft {
  const [state, dispatch] = useReducer(reduce, value, (initial): DraftState => ({ draft: initial, base: initial, seen: initial }));
  if (state.seen !== value) dispatch({ type: "host", value });

  const setDraft = useCallback((text: string) => dispatch({ type: "edit", text }), []);
  const send = useCallback(async (text: string, call: (text: string) => Sent): Promise<void | boolean> => {
    dispatch({ type: "sent", text });
    let accepted = false;
    try {
      const result = await call(text);
      accepted = result !== false;
      return result;
    } finally {
      dispatch({ type: "answered", text, accepted });
    }
  }, []);

  return [state.draft, setDraft, send];
}
