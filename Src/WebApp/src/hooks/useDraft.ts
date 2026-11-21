import { useCallback, useReducer, useRef } from "react";

/** What committing a box answers: `false` is a refusal; anything else, or nothing, means the host took the call in. */
export type Committed = void | boolean | Promise<void | boolean>;

interface DraftState {
  /** What the box shows. */
  draft: string;
  /** The host's value as of the last render. */
  seen: string;
  /** How many sends from this box the host has not answered yet. */
  inflight: number;
  /** The newest send: its id, its text, and whether the host took it in (null until it answers). */
  sentId: number;
  sentText: string;
  sentAccepted: boolean | null;
}

type DraftAction =
  | { type: "host" }
  | { type: "edit"; text: string }
  | { type: "sent"; id: number; text: string }
  | { type: "answered"; id: number; accepted: boolean };

/**
 * The host's value as it is now. While a send from this box is in flight the typed text is kept, whatever the host's
 * value does: it is the answer to that send, or to an older one, and the last send to settle reconciles the box. With
 * none in flight the box follows the host, as it always has, even over text that was typed and never sent.
 */
function follow(state: DraftState, host: string): DraftState {
  if (state.seen === host) return state;
  return { ...state, seen: host, draft: state.inflight > 0 ? state.draft : host };
}

function reduce(previous: DraftState, action: DraftAction, host: string): DraftState {
  // The host's value first: it is what the render that handles this action has been given.
  const state = follow(previous, host);
  switch (action.type) {
    case "host":
      return state;
    case "edit":
      return { ...state, draft: action.text };
    case "sent":
      return { ...state, inflight: state.inflight + 1, sentId: action.id, sentText: action.text, sentAccepted: null };
    case "answered": {
      const inflight = state.inflight - 1;
      const sentAccepted = action.id === state.sentId ? action.accepted : state.sentAccepted;
      // An older send's answer changes nothing the box shows while a newer one is outstanding.
      if (inflight > 0) return { ...state, inflight, sentAccepted };
      // The last send has settled, and the box is reconciled with the host: the newest text is replaced by what the
      // host holds now (it normalised it, or kept the value it had) unless the user typed since. A refused send leaves the
      // text as it is, an edit the host has not seen, until the host's value next changes.
      const draft = sentAccepted === true && state.draft === state.sentText ? state.seen : state.draft;
      return { ...state, inflight, sentAccepted, draft };
    }
  }
}

/** The text of a box and the way to send it: `send(text, call)` makes `call(text)` and answers what it answered. */
export type Draft = [draft: string, setDraft: (draft: string) => void, send: (text: string, call: (text: string) => Committed) => Promise<void | boolean>];

/**
 * A text box the user types in and the host also changes. The box follows the host's value, the page commits what was
 * typed when the box is left, and text typed after a commit is not lost to the answer to it.
 *
 * Commit through `send`. From the first send until the last one has settled, nothing the host's value does to the box
 * overwrites what was typed since: the echo of a commit arrives while the user is still typing. When the last send
 * settles the box is reconciled with the host: if the user typed nothing since, it shows the host's value (the host may
 * have normalised the text, or kept the value it had). A refused call (answers `false`, or throws) leaves the text as it
 * is, to be sent again or fixed, until the host's value next changes; then the box follows the host, so text the host
 * refused is never sent over a value that was loaded after.
 *
 * It assumes of the caller that the host's reply is part of `value` by the time `call` settles: useInput and
 * useProtection apply the reply snapshot before the call answers. Whatever reaches `value` in the same render as the
 * answer is taken into account. A value that only follows later is an ordinary change of the host's value: the box shows
 * the value it had until then, and then follows it.
 */
export function useDraft(value: string): Draft {
  // The reducer reads the host's value from this render, so an action is handled with the value that came with it.
  const [state, dispatch] = useReducer(
    (previous: DraftState, action: DraftAction) => reduce(previous, action, value),
    value,
    (initial): DraftState => ({ draft: initial, seen: initial, inflight: 0, sentId: 0, sentText: initial, sentAccepted: null }),
  );
  if (state.seen !== value) dispatch({ type: "host" });

  const nextId = useRef(0);
  const setDraft = useCallback((text: string) => dispatch({ type: "edit", text }), []);
  const send = useCallback(async (text: string, call: (text: string) => Committed): Promise<void | boolean> => {
    const id = ++nextId.current;
    dispatch({ type: "sent", id, text });
    let accepted = false;
    try {
      const result = await call(text);
      accepted = result !== false;
      return result;
    } finally {
      dispatch({ type: "answered", id, accepted });
    }
  }, []);

  return [state.draft, setDraft, send];
}
