import { describe, expect, it } from "vitest";
import type { LogLine } from "../../../types/domain";
import { appendBatch, emptyLog, MAX_LOG_LINES, replaceLog } from "../logModel";
import { line } from "./fixtures";

const ids = (lines: readonly LogLine[]) => lines.map((l) => l.id);
const many = (from: number, to: number): LogLine[] => Array.from({ length: to - from + 1 }, (_, i) => line(from + i, "Info", `line ${from + i}`));

describe("log model", () => {
  it("appendsABatchInOrder", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: [line(1, "Info", "a"), line(2, "Info", "b")] });

    const next = appendBatch(held, { revision: 4, lines: [line(3, "Info", "c"), line(4, "Info", "d")] });

    expect(ids(next.lines)).toEqual([1, 2, 3, 4]);
    expect(next.revision).toBe(4);
  });

  it("keepsTheLinesOfABatchThatArrivesAfterANewerOne_inTheirPlace", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: [line(1, "Info", "a"), line(2, "Info", "b")] });

    // Batches are pushed from the timer's thread and can overtake each other: 5-6 arrives before 3-4.
    const newer = appendBatch(held, { revision: 6, lines: [line(5, "Info", "e"), line(6, "Info", "f")] });
    const both = appendBatch(newer, { revision: 4, lines: [line(3, "Info", "c"), line(4, "Info", "d")] });

    expect(ids(both.lines)).toEqual([1, 2, 3, 4, 5, 6]);
    expect(both.revision).toBe(6);
  });

  it("dropsLinesItAlreadyHolds", () => {
    const held = replaceLog(emptyLog, { revision: 3, lines: [line(1, "Info", "a"), line(2, "Info", "b"), line(3, "Info", "c")] });

    const next = appendBatch(held, { revision: 5, lines: [line(2, "Info", "b"), line(3, "Info", "c"), line(4, "Info", "d")] });
    const again = appendBatch(next, { revision: 5, lines: [line(4, "Info", "d")] });

    expect(ids(again.lines)).toEqual([1, 2, 3, 4]);
  });

  it("dropsABatchThatIsOlderThanTheLastWholeLog", () => {
    const cleared = replaceLog(replaceLog(emptyLog, { revision: 3, lines: [line(1, "Info", "a")] }), { revision: 5, lines: [] });

    // A batch of lines from before the clear, delivered late, must not bring them back.
    const next = appendBatch(cleared, { revision: 4, lines: [line(2, "Info", "stale")] });

    expect(next.lines).toEqual([]);
  });

  it("takesAWholeLogOnlyWhenItIsNotOlderThanTheHeldOne", () => {
    const held = replaceLog(emptyLog, { revision: 8, lines: [line(1, "Info", "a")] });

    expect(replaceLog(held, { revision: 7, lines: [] })).toBe(held);
    expect(replaceLog(held, { revision: 8, lines: [line(1, "Info", "a"), line(2, "Info", "b")] }).lines).toHaveLength(2);
  });

  it("keepsOnlyTheLatestLines_andCountsWhatItTrimmed", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: many(1, MAX_LOG_LINES) });
    expect(held.trimmed).toBe(0);

    const next = appendBatch(held, { revision: 3, lines: many(MAX_LOG_LINES + 1, MAX_LOG_LINES + 10) });

    expect(next.lines).toHaveLength(MAX_LOG_LINES);
    expect(next.lines[0].id).toBe(11);
    expect(next.lines[next.lines.length - 1].id).toBe(MAX_LOG_LINES + 10);
    expect(next.trimmed).toBe(10);
  });

  it("trimsAHugeWholeLog_onTheWayIn", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: many(1, MAX_LOG_LINES + 250) });

    expect(held.lines).toHaveLength(MAX_LOG_LINES);
    expect(held.lines[0].id).toBe(251);
    expect(held.trimmed).toBe(250);
  });

  it("doesNotBringBackALineItTrimmed_whenALateBatchCarriesIt", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: many(1, MAX_LOG_LINES + 5) });

    const next = appendBatch(held, { revision: 2 + 1, lines: [line(2, "Info", "line 2")] });

    expect(next.lines[0].id).toBe(6);
    expect(next.lines).toHaveLength(MAX_LOG_LINES);
  });

  it("forgetsTheTrimCount_whenTheLogIsCleared", () => {
    const held = replaceLog(emptyLog, { revision: 2, lines: many(1, MAX_LOG_LINES + 5) });

    const cleared = replaceLog(held, { revision: 3, lines: [] });

    expect(cleared.trimmed).toBe(0);
    expect(appendBatch(cleared, { revision: 4, lines: [line(MAX_LOG_LINES + 6, "Info", "next")] }).lines).toHaveLength(1);
  });
});
