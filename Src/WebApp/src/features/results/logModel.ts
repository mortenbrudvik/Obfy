import type { LogAppended, LogLine, LogSnapshot } from "../../types/domain";
import { isProblem } from "./logFormat";

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
  /** The id of the newest non-problem line let go: non-problem line ids only rise, so nothing at or below it is taken back in. */
  trimmedId: number;
  /** The id of the newest problem line (Warning/Error) let go when problems themselves exceeded `MAX_LOG_LINES`. */
  trimmedProblemId: number;
}

export const emptyLog: Held = { revision: 0, floor: 0, lines: [], trimmed: 0, trimmedId: 0, trimmedProblemId: 0 };

/** Lets go of the oldest lines beyond the bound, dropping non-problem lines first so warnings and errors stay intact. */
function bounded(held: Held): Held {
  const excess = held.lines.length - MAX_LOG_LINES;
  if (excess <= 0) return held;

  let nonProblemCount = 0;
  for (const line of held.lines) {
    if (!isProblem(line)) nonProblemCount++;
  }

  let nonProblemsRemaining = Math.min(excess, nonProblemCount);
  let problemsRemaining = excess - nonProblemsRemaining;
  const kept: LogLine[] = [];
  let maxDroppedId = held.trimmedId;
  let maxDroppedProblemId = held.trimmedProblemId;

  for (const line of held.lines) {
    if (isProblem(line)) {
      if (problemsRemaining > 0) {
        problemsRemaining--;
        if (line.id > maxDroppedProblemId) maxDroppedProblemId = line.id;
      } else {
        kept.push(line);
      }
    } else if (nonProblemsRemaining > 0) {
      nonProblemsRemaining--;
      if (line.id > maxDroppedId) maxDroppedId = line.id;
    } else {
      kept.push(line);
    }
  }

  return {
    ...held,
    lines: kept,
    trimmed: held.trimmed + excess,
    trimmedId: maxDroppedId,
    trimmedProblemId: maxDroppedProblemId,
  };
}

/**
 * Adds the lines of a batch that the log does not hold yet. A batch at or below the last whole log is stale and changes
 * nothing. Batches can overtake each other on the way: the lines of one that arrives after a newer batch are put where
 * their ids belong, not dropped.
 */
export function appendBatch(held: Held, batch: LogAppended): Held {
  if (batch.revision <= held.floor) return held;
  const revision = Math.max(held.revision, batch.revision);
  const lastId = held.lines.length > 0 ? held.lines[held.lines.length - 1].id : Math.max(held.trimmedId, held.trimmedProblemId);
  const candidates = batch.lines.filter((line) =>
    isProblem(line) ? line.id > held.trimmedProblemId : line.id > held.trimmedId,
  );

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
  return bounded({
    revision: snapshot.revision,
    floor: snapshot.revision,
    lines: snapshot.lines,
    trimmed: 0,
    trimmedId: 0,
    trimmedProblemId: 0,
  });
}
