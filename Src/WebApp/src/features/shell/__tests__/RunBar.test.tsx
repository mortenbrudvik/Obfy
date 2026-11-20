import { fireEvent, render, screen } from "@testing-library/react";
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
