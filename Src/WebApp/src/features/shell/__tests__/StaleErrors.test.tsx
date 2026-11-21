import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { mockBridge } from "../../../test/bridge";
import { emptyFiles, idleRun, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { RunSnapshot } from "../../../types/domain";
import Shell from "../Shell";

function startHost(run: RunSnapshot, extra: Record<string, (params: unknown) => unknown> = {}) {
  return mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "run.get": () => run,
    "files.get": () => emptyFiles,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
    ...extra,
  });
}

const refused = () => {
  throw new Error("Add at least one included file.");
};

describe("an error that no longer applies", () => {
  it("leavesTheRunBar_whenTheRunStateChangesAfterARefusedStart", async () => {
    const host = startHost(readyRun, { "run.start": refused });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/of 0 files/);

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    expect((await screen.findByRole("alert")).textContent).toContain("Could not start the run");

    // The user fixes the cause: the host's next snapshot says a run can start, and the old complaint must go.
    host.push("run.changed", { ...readyRun, revision: 3, outputHint: "App.obfuscated.dll next to App.dll" });

    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByText(/Output: App\.obfuscated\.dll next to App\.dll/)).toBeTruthy();
  });

  it("showsTheBlockersReason_ratherThanAnOldRefusal_afterTheStateChanged", async () => {
    const host = startHost(readyRun, { "run.start": refused });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/of 0 files/);
    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    await screen.findByRole("alert");

    host.push("run.changed", { ...idleRun, revision: 3, blockedReason: "Fix the watermark id." });

    expect(await screen.findByText("Fix the watermark id.")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("keepsAFreshRefusal_whenNothingChangedSince", async () => {
    startHost(readyRun, { "run.start": refused });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/of 0 files/);

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    expect((await screen.findByRole("alert")).textContent).toContain("Add at least one included file");
  });

  it("dropsAFailedCancel_whenTheRunEnds", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 1 };
    const host = startHost(running, { "run.cancel": () => { throw new Error("Nothing to cancel."); } });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/Processing a\.dll/);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect((await screen.findByRole("alert")).textContent).toContain("Could not cancel the run");

    host.push("run.changed", { ...readyRun, revision: 6, status: "Done" });

    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull(), { timeout: 3000 });
  });

  it("keepsAFailedCancel_whenAProgressPushWithTheSameStatusArrives", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 2, completed: 0 };
    const host = startHost(running, { "run.cancel": () => { throw new Error("Nothing to cancel."); } });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/Processing a.dll/);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect((await screen.findByRole("alert")).textContent).toContain("Could not cancel the run");

    // The host bumps the revision with every progress report (RunCoordinator.Report): the run is still running, and the
    // user has not yet read that the cancel failed.
    host.push("run.changed", { ...running, revision: 7, currentFile: "b.dll", completed: 1, progress: 50 });

    expect(screen.getByText(/Processing b.dll/)).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toContain("Could not cancel the run");
  });

  it("dropsAFailedCancel_whenTheUserTriesAgainAndItWorks", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 1 };
    let refuse = true;
    startHost(running, {
      "run.cancel": () => {
        if (refuse) throw new Error("Nothing to cancel.");
        return running;
      },
    });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/Processing a.dll/);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await screen.findByRole("alert");

    refuse = false;
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  });

  it("clearsAShortcutError_whenTheUserMovesToAnotherScreen", async () => {
    startHost(readyRun, { "files.pick": () => { throw new Error("The picker failed."); } });
    const { rerender } = render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/of 0 files/);

    fireEvent.keyDown(window, { key: "o", ctrlKey: true });
    expect((await screen.findByRole("alert")).textContent).toContain("Could not add files");

    rerender(<Shell screen="protection" onNavigate={vi.fn()}>content</Shell>);

    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("clearsAShortcutError_whenTheFileListChanges", async () => {
    const host = startHost(readyRun, { "files.pick": () => { throw new Error("The picker failed."); } });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/of 0 files/);
    fireEvent.keyDown(window, { key: "o", ctrlKey: true });
    await screen.findByRole("alert");

    host.push("files.changed", { ...emptyFiles, revision: 4 });

    expect(screen.queryByRole("alert")).toBeNull();
  });
});
