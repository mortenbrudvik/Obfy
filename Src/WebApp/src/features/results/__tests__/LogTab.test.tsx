import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import LogTab from "../LogTab";
import type { LogOptions } from "../logFormat";
import { line } from "./fixtures";

const lines = [
  line(1, "Info", "Loading A.dll"),
  line(2, "Warning", "Slow disk"),
  line(3, "Success", "Done A.dll"),
  line(4, "Info", "———"),
  line(5, "Error", "B.dll failed"),
  line(6, "Info", "Loading C.dll"),
];

const defaults: LogOptions = { problemsOnly: false, timestamps: true, wrap: false };

function Harness({ initial, onClear = () => {} }: { initial?: Partial<LogOptions>; onClear?: () => void }) {
  const [options, setOptions] = useState<LogOptions>({ ...defaults, ...initial });
  return <LogTab lines={lines} options={options} onOptionsChange={setOptions} onClear={onClear} />;
}

describe("LogTab", () => {
  beforeEach(() => vi.clearAllMocks());

  it("showsEveryLine_withTimestampsAndTheRunSeparator", () => {
    render(<Harness />);

    expect(screen.getByText("Loading A.dll")).toBeTruthy();
    expect(screen.getByText("12:00:01.000")).toBeTruthy();
    expect(screen.getByLabelText("Start of a new run").textContent).toBe("———");
    // Roles, so a screen reader exposes the names the labels give.
    expect(screen.getByRole("separator", { name: "Start of a new run" })).toBeTruthy();
    expect(screen.getByRole("log", { name: "Log" })).toBeTruthy();
    expect(screen.getByText("Warning").className).toContain("lv-warning");
  });

  it("showsOnlyWarningsAndErrors_inProblemsOnly", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Problems only" }));

    expect(screen.getByText("Slow disk")).toBeTruthy();
    expect(screen.getByText("B.dll failed")).toBeTruthy();
    expect(screen.queryByText("Loading A.dll")).toBeNull();
    expect(screen.queryByText("Done A.dll")).toBeNull();
    expect(screen.queryByText("Loading C.dll")).toBeNull();
    expect(screen.getByRole("button", { name: "Problems only" }).getAttribute("aria-pressed")).toBe("true");
    // The separator between the two runs' problems stays.
    expect(screen.getAllByLabelText("Start of a new run")).toHaveLength(1);
  });

  it("hidesTimestamps_andWrapsOnRequest", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("switch", { name: "Timestamps" }));
    expect(screen.queryByText("12:00:01.000")).toBeNull();

    const log = screen.getByLabelText("Log");
    expect(log.className).toContain("nowrap");
    fireEvent.click(screen.getByRole("switch", { name: "Wrap" }));
    expect(log.className.split(" ")).toContain("wrap");
    expect(log.className.split(" ")).not.toContain("nowrap");
  });

  it("copiesTheVisibleLogText", async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    render(<Harness initial={{ problemsOnly: true, timestamps: false }} />);

    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    expect(writeText).toHaveBeenCalledWith("Warning Slow disk\n———\nError B.dll failed");
    expect(await screen.findByRole("button", { name: "Copied" })).toBeTruthy();
  });

  it("clearsTheLog", () => {
    const onClear = vi.fn();
    render(<Harness onClear={onClear} />);

    fireEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(onClear).toHaveBeenCalled();
  });

  it("saysWhenThereAreNoLines", () => {
    render(<LogTab lines={[]} options={defaults} onOptionsChange={() => {}} onClear={() => {}} />);

    expect(screen.getByText("Nothing has been logged yet.")).toBeTruthy();
  });
});

describe("visibleLines", () => {
  it("dropsSeparatorsThatWouldLeadTrailOrRepeat_inProblemsOnly", async () => {
    const { visibleLines } = await import("../logFormat");
    const sep = (id: number) => line(id, "Info", "———");

    const shown = visibleLines(
      [sep(1), line(2, "Info", "ok"), sep(3), line(4, "Error", "bad"), sep(5), sep(6), line(7, "Info", "ok"), sep(8), line(9, "Warning", "hmm"), sep(10)],
      true,
    );

    expect(shown.map((l) => l.message)).toEqual(["bad", "———", "hmm"]);
  });
});
