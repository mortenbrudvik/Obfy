import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import RunBar from "../RunBar";
import type { RunSnapshot } from "../../../types/domain";

const idle: RunSnapshot = {
  revision: 1,
  canStart: true,
  blockedReason: null,
  mode: "Single",
  modeExplanation: "Each file is protected on its own.",
  outputHint: "obfy.obfuscated.dll next to obfy.dll",
  progress: 0,
  indeterminate: false,
  currentFile: null,
  completed: 0,
  total: 0,
  failed: 0,
  status: "Idle",
  message: "",
};

const summary = { includedCount: 2, fileCount: 3, level: "Standard" };

function renderBar(run: RunSnapshot, props: { onStart?: () => void; onCancel?: () => void; error?: string | null } = {}) {
  return render(<RunBar run={run} {...summary} onStart={props.onStart ?? vi.fn()} onCancel={props.onCancel ?? vi.fn()} error={props.error ?? null} />);
}

describe("RunBar", () => {
  it("showsTheBlockedReason_andDisablesObfuscate_whenItCannotStart", () => {
    renderBar({ ...idle, canStart: false, blockedReason: "Add at least one included file." });

    expect(screen.getByText("Add at least one included file.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Obfuscate" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText(/2 of 3 files/)).toBeNull();
  });

  it("showsTheSummary_andAnEnabledObfuscate_whenItCanStart", () => {
    const onStart = vi.fn();
    renderBar(idle, { onStart });

    expect(screen.getByText(/2 of 3 files · Standard · Output: obfy\.obfuscated\.dll next to obfy\.dll/)).toBeTruthy();
    const button = screen.getByRole("button", { name: "Obfuscate" }) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    fireEvent.click(button);
    expect(onStart).toHaveBeenCalledTimes(1);
  });

  it("showsProgressAndCancel_whileRunning", () => {
    const onCancel = vi.fn();
    renderBar(
      { ...idle, canStart: false, status: "Running", progress: 50, currentFile: "App.dll", completed: 1, total: 2, message: "Processing App.dll" },
      { onCancel },
    );

    expect(screen.getByText(/Processing App\.dll — 2 of 2/)).toBeTruthy();
    const bar = screen.getByRole("progressbar");
    expect(bar.getAttribute("aria-valuenow")).toBe("50");
    expect(screen.queryByRole("button", { name: "Obfuscate" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("showsAnIndeterminateBar_whenTheRunCannotReportProgress", () => {
    renderBar({ ...idle, canStart: false, status: "Running", indeterminate: true, currentFile: null, total: 3, message: "Merging" });

    expect(screen.getByRole("progressbar").hasAttribute("aria-valuenow")).toBe(false);
    expect(screen.getByText("Merging")).toBeTruthy();
  });

  it("showsAFailedStart_asAnAlert", () => {
    renderBar(idle, { error: "Could not start the run (nothing included)." });

    expect(screen.getByRole("alert").textContent).toContain("Could not start the run");
  });
});

describe("RunBar's blocked reason", () => {
  const blockedBecause = (reason: string): RunSnapshot => ({ ...idle, canStart: false, blockedReason: reason });
  const props = { ...summary, onStart: vi.fn(), onCancel: vi.fn(), error: null };

  it("announcesTheReason_inAPoliteStatusRegion_soAScreenReaderUserHearsWhyObfuscateIsOff", () => {
    renderBar(blockedBecause("Add at least one included file."));

    const status = screen.getByRole("status");

    expect(status.textContent).toBe("Add at least one included file.");
  });

  it("keepsTheSameRegionMounted_whenTheReasonChanges_soItIsAnnouncedAsAChangeNotAsANewElement", () => {
    const { rerender } = render(<RunBar run={blockedBecause("Add at least one included file.")} {...props} />);
    const status = screen.getByRole("status");

    rerender(<RunBar run={blockedBecause("Fix the watermark id.")} {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("Fix the watermark id.");
  });

  it("hasTheRegionBeforeAReasonAppears_asAScreenReaderOnlyHearsContentPutIntoARegionItAlreadyKnows", () => {
    const { rerender } = render(<RunBar run={idle} {...props} />);
    const status = screen.getByRole("status");
    expect(status.textContent).toBe("");

    rerender(<RunBar run={blockedBecause("Add at least one included file.")} {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("Add at least one included file.");
  });

  it("emptiesTheRegion_whenTheRunCanStartAgain", () => {
    const { rerender } = render(<RunBar run={blockedBecause("Add at least one included file.")} {...props} />);
    const status = screen.getByRole("status");

    rerender(<RunBar run={idle} {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("");
    expect(screen.getByText(/2 of 3 files/)).toBeTruthy();
  });

  it("doesNotMakeTheSummaryALiveRegion_asItChangesWithEveryFileAndLevel", () => {
    renderBar(idle);

    expect(screen.getByText(/2 of 3 files/).closest("[role=status]")).toBeNull();
    expect(within(screen.getByRole("status")).queryByText(/2 of 3 files/)).toBeNull();
  });

  it("hasTheRegionBeforeTheHostHasAnswered_soAReasonOnFirstLoadIsAnnouncedAsAChange", () => {
    const { rerender } = render(<RunBar run={null} {...props} />);
    const status = screen.getByRole("status");
    expect(status.textContent).toBe("");
    expect(screen.getByText("Connecting…").closest("[role=status]")).toBeNull();

    rerender(<RunBar run={blockedBecause("Add at least one included file.")} {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("Add at least one included file.");
  });

  it("announcesThatTheAppCannotBeReached_inTheSameRegion", () => {
    const { rerender } = render(<RunBar run={null} {...props} />);
    const status = screen.getByRole("status");

    rerender(<RunBar run={null} unreachable {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("Could not reach the app.");
  });

  it("hasTheRegionWhileRunning_soAFaultWhenItEndsIsAnnouncedAsAChange", () => {
    const running: RunSnapshot = { ...idle, canStart: false, status: "Running", currentFile: "App.dll", total: 1, message: "Processing App.dll" };
    const { rerender } = render(<RunBar run={running} {...props} />);
    const status = screen.getByRole("status");
    expect(status.textContent).toBe("");
    expect(screen.getByText(/Processing App.dll — 1 of 1/).closest("[role=status]")).toBeNull();

    rerender(<RunBar run={{ ...idle, status: "Faulted", message: "Obfuscation failed: access denied." }} {...props} />);

    expect(screen.getByRole("status")).toBe(status);
    expect(status.textContent).toBe("Obfuscation failed: access denied.");
  });

  it("keepsOneRegion_fromTheFirstLoadThroughARunToItsEnd", () => {
    const { rerender } = render(<RunBar run={null} {...props} />);
    const status = screen.getByRole("status");

    rerender(<RunBar run={idle} {...props} />);
    rerender(<RunBar run={{ ...idle, canStart: false, status: "Running", currentFile: "App.dll", total: 1 }} {...props} />);
    rerender(<RunBar run={blockedBecause("Add at least one included file.")} {...props} />);

    expect(screen.getAllByRole("status")).toEqual([status]);
    expect(status.textContent).toBe("Add at least one included file.");
  });

  it("leavesTheAlertToAFailedStart_andTheRegionEmpty", () => {
    renderBar(blockedBecause("Add at least one included file."), { error: "Could not start the run (nothing included)." });

    expect(screen.getByRole("alert").textContent).toContain("Could not start the run");
    expect(screen.getByRole("status").textContent).toBe("");
  });
});
