import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { FileRow, InputSnapshot, RunSnapshot } from "../../../types/domain";
import Shell from "../../shell/Shell";
import FilesPage from "../FilesPage";

const row: FileRow = {
  id: "a", name: "a.dll", path: "C:/src/a.dll", origin: "Added", kind: "Assembly", size: 2048, sizeText: "2 KB",
  included: true, status: "Pending", skipReason: null, outputPath: null, error: null,
};
const withFile = (over: Partial<InputSnapshot> = {}): InputSnapshot => ({
  ...emptyFiles, revision: 2, files: [row], hasIncludedFiles: true, outputHint: "a.obfuscated.dll next to a.dll", ...over,
});

/** The host as the page sees it, with the order in which the calls that matter (not the reads) arrived. */
function host(handlers: Record<string, (params: unknown) => unknown>, run: RunSnapshot = readyRun) {
  const order: string[] = [];
  mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "files.get": () => withFile(),
    "run.get": () => run,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
    ...Object.fromEntries(
      Object.entries(handlers).map(([key, handler]) => [
        key,
        (params: unknown) => {
          if (!key.endsWith(".get")) order.push(key);
          return handler(params);
        },
      ]),
    ),
  });
  return order;
}

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

const later = () => new Promise((resolve) => setTimeout(resolve, 20));

async function renderFiles() {
  render(
    <Shell screen="files" onNavigate={vi.fn()}>
      <FilesPage />
    </Shell>,
  );
  await screen.findByRole("table");
}

describe("a shortcut pressed while the output boxes still hold text", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("sendsTheTypedOutputFolder_andOnlyThenStartsTheRun_onCtrlEnter", async () => {
    let release!: () => void;
    const answered = new Promise<void>((resolve) => (release = resolve));
    const order = host({
      "output.set": async () => {
        await answered;
        return withFile({ revision: 3, outputFolder: "D:/protected" });
      },
      "run.start": () => ({ ...readyRun, status: "Running" }),
    });
    await renderFiles();
    const box = screen.getByLabelText("Output folder") as HTMLInputElement;
    fireEvent.change(box, { target: { value: "D:/protected" } });

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    // The run would have used the old folder; the shortcut sends the typed one first and waits for the host.
    await waitFor(() => expect(calls("output", "set")).toHaveLength(1), { timeout: 3000 });
    expect(calls("output", "set")[0][2]).toEqual({ folder: "D:/protected" });
    await later();
    expect(order).toEqual(["output.set"]);

    release();

    await waitFor(() => expect(order).toEqual(["output.set", "run.start"]), { timeout: 3000 });
    expect(box.value).toBe("D:/protected");
  });

  it("sendsTheTypedSymbolMapPath_andOnlyThenStartsTheRun_onCtrlEnter", async () => {
    const order = host({
      "files.get": () => withFile({ writeSymbolMap: true, symbolMapPath: "" }),
      "output.setSymbolMap": () => withFile({ revision: 3, writeSymbolMap: true, symbolMapPath: "D:/maps/a.map" }),
      "run.start": () => ({ ...readyRun, status: "Running" }),
    });
    await renderFiles();
    fireEvent.change(screen.getByLabelText("Symbol map file"), { target: { value: "D:/maps/a.map" } });

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await waitFor(() => expect(order).toEqual(["output.setSymbolMap", "run.start"]), { timeout: 3000 });
    expect(calls("output", "setSymbolMap")[0][2]).toEqual({ path: "D:/maps/a.map" });
  });

  it("doesNotStartTheRun_whenTheHostRefusesTheTypedFolder", async () => {
    const order = host({
      "output.set": () => {
        throw new Error("output.invalidFolder");
      },
      "run.start": () => ({ ...readyRun, status: "Running" }),
    });
    await renderFiles();
    fireEvent.change(screen.getByLabelText("Output folder"), { target: { value: "Z:/nowhere" } });

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await screen.findByText(/Could not change the output/);
    await later();
    expect(order).toEqual(["output.set"]);
  });

  it("sendsNothing_whenTheBoxesHoldNothingNew", async () => {
    const order = host({ "output.set": () => withFile(), "run.start": () => ({ ...readyRun, status: "Running" }) });
    await renderFiles();

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await waitFor(() => expect(order).toEqual(["run.start"]), { timeout: 3000 });
  });

  it("doesNotSendAHiddenSymbolMapDraft_afterTheOptionWasSwitchedOff", async () => {
    const order = host({
      "files.get": () => withFile({ writeSymbolMap: true, symbolMapPath: "" }),
      "output.setSymbolMap": () => withFile({ revision: 3, writeSymbolMap: false }),
      "run.start": () => ({ ...readyRun, status: "Running" }),
    });
    await renderFiles();
    fireEvent.change(screen.getByLabelText("Symbol map file"), { target: { value: "D:/maps/a.map" } });
    fireEvent.click(screen.getByLabelText("Write a symbol map"));
    await waitFor(() => expect(screen.queryByLabelText("Symbol map file")).toBeNull(), { timeout: 3000 });
    order.length = 0;

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await waitFor(() => expect(order).toEqual(["run.start"]), { timeout: 3000 });
  });
});
