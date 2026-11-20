import raw from "./guide.json";

// The Guide's copy. A paragraph, list item or note is plain text with a little inline markup:
// **bold**, `monospace`, and {Ctrl+Enter} for keycaps (one per key, split on "+").

export interface Shortcut {
  label: string;
  keys: string[];
}

export type GuideBlock =
  | { type: "p"; text: string }
  | { type: "h3"; text: string }
  | { type: "ul"; items: string[] }
  | { type: "note"; title: string; text: string }
  | { type: "shortcuts"; items: Shortcut[] };

export interface GuideTopic {
  id: string;
  title: string;
  blocks: GuideBlock[];
}

export const guideTopics = (raw as { topics: GuideTopic[] }).topics;

/** The topic opened from the sidebar and F1. */
export const FIRST_GUIDE_TOPIC = guideTopics[0].id;
