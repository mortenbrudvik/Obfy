import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import FilesPage from "../features/files/FilesPage";
import ProtectionPage from "../features/protection/ProtectionPage";
import { protectionSnapshot } from "../features/protection/__tests__/fixtures";
import RunningView from "../features/results/RunningView";
import { line } from "../features/results/__tests__/fixtures";
import Shell from "../features/shell/Shell";
import { apiClient } from "../interop/WebViewApiClient";
import { mockBridge } from "../test/bridge";
import { emptyFiles, idleRun, notRunResults, readyRun, standardSettings } from "../test/snapshots";
import type { RunSnapshot } from "../types/domain";

const runningSnapshot: RunSnapshot = {
  ...readyRun,
  canStart: false,
  status: "Running",
  currentFile: "App.dll",
  total: 1,
  progress: 25,
};

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

describe("ReviewFixes", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("FilesPage_showsNoticesWhenListIsEmpty", async () => {
    mockBridge({
      "files.get": () => ({
        ...emptyFiles,
        notices: [{ level: "Warning", title: "Could not load configuration", message: "Invalid JSON syntax." }],
      }),
      "run.get": () => idleRun,
    });

    render(<FilesPage />);

    expect(await screen.findByText("Could not load configuration")).toBeTruthy();
    expect(screen.getByText("Invalid JSON syntax.")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "What do you want to protect?" })).toBeTruthy();
  });

  it("RunningView_keepsAutoScrollingPast200Lines", () => {
    const initial = Array.from({ length: 200 }, (_, i) => line(i + 1, "Info", `Line ${i + 1}`));
    const { rerender } = render(<RunningView files={[]} log={initial} />);

    const logBox = screen.getByRole("log", { name: "Log" });
    Object.defineProperty(logBox, "scrollHeight", { value: 2500, configurable: true });
    logBox.scrollTop = 100;

    // Adding line 201 keeps tail.length at 200 while changing the last line's id.
    const next = [...initial.slice(1), line(201, "Info", "Line 201")];
    rerender(<RunningView files={[]} log={next} />);

    expect(logBox.scrollTop).toBe(2500);
  });

  it("ProtectionPage_disablesControlsWhileRunning_exceptSave", async () => {
    mockBridge({
      "settings.get": () => protectionSnapshot({ level: "Standard" }),
      "run.get": () => runningSnapshot,
    });

    render(<ProtectionPage />);

    const openBtn = (await screen.findByRole("button", { name: "Open…" })) as HTMLButtonElement;
    const saveBtn = screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;
    const standardRadio = screen.getByRole("radio", { name: /^Standard/ }) as HTMLButtonElement;
    const runtimeSelect = screen.getByLabelText("Target runtime") as HTMLSelectElement;
    const controlFlowSwitch = screen.getByRole("switch", { name: "Control flow" }) as HTMLInputElement;

    await waitFor(() => expect(openBtn.disabled).toBe(true));
    expect(saveBtn.disabled).toBe(false);
    expect(standardRadio.disabled).toBe(true);
    expect(runtimeSelect.disabled).toBe(true);
    expect(controlFlowSwitch.disabled).toBe(true);
  });

  it("Shell_ignoresCtrlOAndCtrlLWhileRunning_butAllowsCtrlS", async () => {
    mockBridge({
      "app.info": () => ({ version: "1.2.3" }),
      "run.get": () => runningSnapshot,
      "files.get": () => emptyFiles,
      "settings.get": () => standardSettings,
      "results.summary": () => notRunResults,
      "files.pick": () => emptyFiles,
      "settings.openConfig": () => standardSettings,
      "settings.saveConfig": () => standardSettings,
    });

    render(
      <Shell screen="files" onNavigate={vi.fn()}>
        content
      </Shell>,
    );
    await screen.findByText(/Processing App\.dll/);

    fireEvent.keyDown(window, { key: "o", ctrlKey: true });
    fireEvent.keyDown(window, { key: "l", ctrlKey: true });
    fireEvent.keyDown(window, { key: "s", ctrlKey: true });

    await waitFor(() => expect(calls("settings", "saveConfig")).toHaveLength(1));
    expect(calls("files", "pick")).toHaveLength(0);
    expect(calls("settings", "openConfig")).toHaveLength(0);
  });
});
