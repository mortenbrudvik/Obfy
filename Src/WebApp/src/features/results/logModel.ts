import type { LogAppended, LogLine, LogSnapshot } from "../../types/domain";

/**
 * How many lines the page keeps. A run at debug level writes thousands, the list is drawn line by line, and every
 * batch copies it, so the page holds the latest ones and says how many older ones it let go of. The host keeps the lot.
 */
export const MAX_LOG_LINES = 5000;

/** What the page holds of the log. */
export interface Held {
  /** The newest revision the page has taken in. */
  revision: number;
  /** The revision of the last whole log taken (`log.get`, `log.cleared`): a batch at or below it is already in it, or from before a clear. */
  floor: number;
  lines: LogLine[];
  /** How many of the oldest lines were let go to stay within `MAX_LOG_LINES`, since the log was last cleared. */
  trimmed: number;
  /** The id of the newest line let go: line ids only rise, so nothing at or below it is taken back in. */
  trimmedId: number;
}

export const emptyLog: Held = { revision: 0, floor: 0, lines: [], trimmed: 0, trimmedId: 0 };

/** Lets go of the oldest lines beyond the bound. */
function bounded(held: Held): Held {
  const excess = held.lines.length - MAX_LOG_LINES;
  if (excess <= 0) return held;
  return { ...held, lines: held.lines.slice(excess), trimmed: held.trimmed + excess, trimmedId: held.lines[excess - 1].id };
}

/**
 * Adds the lines of a batch that the log does not hold yet. A batch at or below the last whole log is stale and changes
 * nothing. Batches can overtake each other on the way: the lines of one that arrives after a newer batch are put where
 * their ids belong, not dropped.
 */
export function appendBatch(held: Held, batch: LogAppended): Held {
  if (batch.revision <= held.floor) return held;
  const revision = Math.max(held.revision, batch.revision);
  const lastId = held.lines.length > 0 ? held.lines[held.lines.length - 1].id : held.trimmedId;
  const candidates = batch.lines.filter((line) => line.id > held.trimmedId);

  if (candidates.every((line) => line.id > lastId)) {
    return bounded({ ...held, revision, lines: candidates.length > 0 ? [...held.lines, ...candidates] : held.lines });
  }

  const have = new Set(held.lines.map((line) => line.id));
  const fresh = candidates.filter((line) => !have.has(line.id));
  return bounded({ ...held, revision, lines: fresh.length > 0 ? [...held.lines, ...fresh].sort((a, b) => a.id - b.id) : held.lines });
}

/** Takes a whole log in place of the held one, unless the held one is newer. */
export function replaceLog(held: Held, snapshot: LogSnapshot): Held {
  if (snapshot.revision < held.revision) return held;
  return bounded({ revision: snapshot.revision, floor: snapshot.revision, lines: snapshot.lines, trimmed: 0, trimmedId: 0 });
}
