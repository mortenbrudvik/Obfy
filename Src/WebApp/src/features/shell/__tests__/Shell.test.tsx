import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, idleRun, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { FileRow, RunSnapshot } from "../../../types/domain";
import Shell from "../Shell";

const file = (name: string, included: boolean): FileRow => ({
  id: name, name, path: `C:\\${name}`, origin: "Added", kind: "Assembly", size: 1, sizeText: "1 B",
  included, status: "Pending", skipReason: null, outputPath: null, error: null,
});

function startHost(run: RunSnapshot, extra: Record<string, (params: unknown) => unknown> = {}) {
  return mockBridge({
    "app.info": () => ({ version: "1.2.3" }),
    "run.get": () => run,
    "files.get": () => ({ ...emptyFiles, files: [file("a.dll", true), file("b.dll", true), file("c.dll", false)] }),
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
    ...extra,
  });
}

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

describe("Shell", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("showsTheSidebarFacts_andTheRunBarSummary", async () => {
    startHost(readyRun);

    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);

    expect(await screen.findByText("2 of 3 files · Standard · Output: App.obfuscated.dll next to App.dll")).toBeTruthy();
    expect(screen.getByLabelText("2 files included").textContent).toBe("2");
    expect(screen.getByText("Standard", { selector: ".meta" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "no results yet" })).toBeTruthy();
    expect(screen.getByText("Obfy 1.2.3")).toBeTruthy();
  });

  it("startsARun_onCtrlEnter_whenItCanStart", async () => {
    startHost(readyRun, { "run.start": () => ({ ...readyRun, status: "Running" }) });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/2 of 3 files/);

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await waitFor(() => expect(calls("run", "start")).toHaveLength(1));
  });

  it("doesNotStart_onCtrlEnter_whenBlocked", async () => {
    startHost(idleRun);
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText("Add at least one included file.");

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    expect(calls("run", "start")).toHaveLength(0);
  });

  it("cancelsTheRun_onEscape_whileRunning_only", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 2, progress: 10 };
    const host = startHost(readyRun, { "run.cancel": () => running });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/2 of 3 files/);

    fireEvent.keyDown(window, { key: "Escape" });
    expect(calls("run", "cancel")).toHaveLength(0);

    host.push("run.changed", running);
    await screen.findByText(/Processing a\.dll/);
    fireEvent.keyDown(window, { key: "Escape" });

    await waitFor(() => expect(calls("run", "cancel")).toHaveLength(1));
  });

  it("showsAFailedStart_asAnAlert", async () => {
    startHost(readyRun, { "run.start": () => { throw new Error("Add at least one included file."); } });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/2 of 3 files/);

    fireEvent.click(screen.getByRole("button", { name: "Obfuscate" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Could not start the run (Add at least one included file).");
  });

  it("opensResults_whenARunCompletesCleanly_butNotWhenItFailsOrIsCancelled", async () => {
    const onNavigate = vi.fn();
    const host = startHost(readyRun);
    render(<Shell screen="files" onNavigate={onNavigate}>content</Shell>);
    await screen.findByText(/2 of 3 files/);

    // run.completed repeats the revision of the run.changed before it: it is a cue, whatever its revision.
    const cancelled: RunSnapshot = { ...readyRun, revision: 9, status: "Cancelled" };
    host.push("run.changed", cancelled);
    host.push("run.completed", cancelled);
    const failedRun: RunSnapshot = { ...readyRun, revision: 10, status: "Done", failed: 1 };
    host.push("run.changed", failedRun);
    host.push("run.completed", failedRun);
    expect(onNavigate).not.toHaveBeenCalled();

    const done: RunSnapshot = { ...readyRun, revision: 11, status: "Done", progress: 100 };
    host.push("run.changed", done);
    host.push("run.completed", done);

    expect(onNavigate).toHaveBeenCalledExactlyOnceWith("results");
  });

  it("addsFiles_onCtrlO_andSavesAndOpensConfig_onCtrlSAndCtrlL", async () => {
    startHost(readyRun, { "files.pick": () => emptyFiles, "settings.saveConfig": () => standardSettings, "settings.openConfig": () => standardSettings });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/2 of 3 files/);

    fireEvent.keyDown(window, { key: "o", ctrlKey: true });
    fireEvent.keyDown(window, { key: "s", ctrlKey: true });
    fireEvent.keyDown(window, { key: "l", ctrlKey: true });

    await waitFor(() => {
      expect(calls("files", "pick")).toHaveLength(1);
      expect(calls("settings", "saveConfig")).toHaveLength(1);
      expect(calls("settings", "openConfig")).toHaveLength(1);
    });
  });

  it("showsTheDropOverlay_whileTheHostSaysFilesAreDragged_andHidesItAfter", async () => {
    const host = startHost(readyRun);
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/2 of 3 files/);
    expect(screen.queryByTestId("drop-overlay")).toBeNull();

    host.push("shell.dragState", { dragging: true });
    expect(screen.getByTestId("drop-overlay").textContent).toBe("Drop files to add them");

    host.push("shell.dragState", { dragging: false });
    expect(screen.queryByTestId("drop-overlay")).toBeNull();
  });

  it("saysFilesCannotBeAdded_inTheDropOverlay_whileARunGoes", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 2, progress: 10 };
    const host = startHost(running);
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    await screen.findByText(/Processing a\.dll/);

    host.push("shell.dragState", { dragging: true });

    expect(screen.getByTestId("drop-overlay").textContent).toBe("Files can't be added while a run is in progress");
  });
});
