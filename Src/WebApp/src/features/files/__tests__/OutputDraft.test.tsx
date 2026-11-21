import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { FileRow, InputSnapshot } from "../../../types/domain";
import Shell from "../../shell/Shell";
import FilesPage from "../FilesPage";

const row: FileRow = {
  id: "a", name: "a.dll", path: "C:/src/a.dll", origin: "Added", kind: "Assembly", size: 2048, sizeText: "2 KB",
  included: true, status: "Pending", skipReason: null, outputPath: null, error: null,
};
const withFile = (over: Partial<InputSnapshot> = {}): InputSnapshot => ({
  ...emptyFiles, revision: 2, files: [row], hasIncludedFiles: true, outputHint: "a.obfuscated.dll next to a.dll", ...over,
});

/** The host with its calls to the output endpoints under the test's control; `push` delivers an event the way the host does. */
function host(handlers: Record<string, (params: unknown) => unknown>, files: InputSnapshot = withFile()) {
  return mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "files.get": () => files,
    "run.get": () => readyRun,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
    "run.start": () => ({ ...readyRun, status: "Running" }),
    ...handlers,
  });
}

const calls = (service: string, method: string) => vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);

async function renderFiles() {
  render(
    <Shell screen="files" onNavigate={vi.fn()}>
      <FilesPage />
    </Shell>,
  );
  await screen.findByRole("table");
}

/** A call the host answers when the test says so: the typing that follows a commit happens before the answer. */
function gate() {
  let release!: () => void;
  const open = new Promise<void>((resolve) => (release = resolve));
  return { open, release };
}

const folderBox = () => screen.getByLabelText("Output folder") as HTMLInputElement;
const mapBox = () => screen.getByLabelText("Symbol map file") as HTMLInputElement;

describe("an output box and the host's answer to what was typed in it", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("keepsTextTypedAfterEnter_whenTheHostEchoesTheCommittedFolder", async () => {
    const answer = gate();
    host({
      "output.set": async () => {
        await answer.open;
        // The hint changes with the folder, which shows the echo has been applied.
        return withFile({ revision: 3, outputFolder: "C:\\out", outputHint: "C:\\out" });
      },
    });
    await renderFiles();
    const box = folderBox();
    fireEvent.change(box, { target: { value: "C:\\out" } });
    fireEvent.keyDown(box, { key: "Enter" });
    await waitFor(() => expect(calls("output", "set")).toHaveLength(1));

    // The box keeps focus after Enter, and the user goes on typing before the host has answered.
    fireEvent.change(box, { target: { value: "C:\\out2" } });
    answer.release();

    await waitFor(() => expect(box.placeholder).toBe("C:\\out"));
    expect(box.value).toBe("C:\\out2");
  });

  it("keepsTextTypedAfterCtrlEnterFlushedTheFolder_whenTheHostEchoesIt", async () => {
    const answer = gate();
    host({
      "output.set": async () => {
        await answer.open;
        return withFile({ revision: 3, outputFolder: "D:/protected", outputHint: "D:/protected" });
      },
    });
    await renderFiles();
    const box = folderBox();
    fireEvent.change(box, { target: { value: "D:/protected" } });
    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    await waitFor(() => expect(calls("output", "set")).toHaveLength(1));

    fireEvent.change(box, { target: { value: "D:/protected2" } });
    answer.release();

    await waitFor(() => expect(box.placeholder).toBe("D:/protected"));
    // The run did start with the folder that was sent; what was typed since is still in the box.
    await waitFor(() => expect(calls("run", "start")).toHaveLength(1));
    expect(box.value).toBe("D:/protected2");
  });

  it("keepsTextTypedAfterEnter_whenTheHostEchoesTheCommittedSymbolMapPath", async () => {
    const answer = gate();
    host(
      {
        "output.setSymbolMap": async () => {
          await answer.open;
          return withFile({ revision: 3, writeSymbolMap: true, symbolMapPath: "D:/maps/a.map", resolvedSymbolMapPath: "D:/maps/a.map" });
        },
      },
      withFile({ writeSymbolMap: true, symbolMapPath: "" }),
    );
    await renderFiles();
    const box = mapBox();
    fireEvent.change(box, { target: { value: "D:/maps/a.map" } });
    fireEvent.keyDown(box, { key: "Enter" });
    await waitFor(() => expect(calls("output", "setSymbolMap")).toHaveLength(1));

    fireEvent.change(box, { target: { value: "D:/maps/a.map2" } });
    answer.release();

    await screen.findByText("Written to D:/maps/a.map");
    expect(box.value).toBe("D:/maps/a.map2");
  });

  it("showsTheHostsValue_whenItNormalisedWhatWasSent_andNothingWasTypedSince", async () => {
    host({ "output.set": () => withFile({ revision: 3, outputFolder: "D:\\out", outputHint: "D:\\out" }) });
    await renderFiles();
    const box = folderBox();
    fireEvent.change(box, { target: { value: "D:/out/ " } });
    fireEvent.blur(box);

    await waitFor(() => expect(box.value).toBe("D:\\out"));
  });

  it("showsTheHostsValue_whenItAnsweredWithTheOldOne_andNothingWasTypedSince", async () => {
    // The host took the call and kept the folder it had: the box must not go on showing a folder the run will not use.
    host({ "output.set": () => withFile({ revision: 3, outputFolder: "C:\\kept", outputHint: "C:\\kept" }) }, withFile({ outputFolder: "C:\\kept" }));
    await renderFiles();
    const box = folderBox();
    expect(box.value).toBe("C:\\kept");
    fireEvent.change(box, { target: { value: "Z:\\other" } });
    fireEvent.blur(box);

    await waitFor(() => expect(calls("output", "set")).toHaveLength(1));
    await waitFor(() => expect(box.value).toBe("C:\\kept"));
  });

  it("keepsWhatWasTyped_whenTheHostRefusesTheCall_soItIsNotSilentlyDropped", async () => {
    host({
      "output.set": () => {
        throw new Error("output.invalidFolder");
      },
    });
    await renderFiles();
    const box = folderBox();
    fireEvent.change(box, { target: { value: "Z:/nowhere" } });
    fireEvent.blur(box);

    await screen.findByText(/Could not change the output/);
    expect(box.value).toBe("Z:/nowhere");
    // It is still an edit the host has not seen: the next shortcut sends it again and does not start the run on the old folder.
    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    await waitFor(() => expect(calls("output", "set")).toHaveLength(2));
    expect(calls("run", "start")).toHaveLength(0);
  });

  it("followsTheHostsFolder_whenItChangesAndTheBoxHoldsNoUncommittedEdit", async () => {
    const events = host({});
    await renderFiles();
    expect(folderBox().value).toBe("");

    // A configuration is loaded, or a preference restored.
    events.push("files.changed", withFile({ revision: 5, outputFolder: "E:/restored" }));

    expect(folderBox().value).toBe("E:/restored");
  });

  it("replacesWhatWasTyped_whenTheHostsFolderChangesAndNothingWasSent", async () => {
    const events = host({});
    await renderFiles();
    fireEvent.change(folderBox(), { target: { value: "C:/typing" } });

    events.push("files.changed", withFile({ revision: 5, outputFolder: "E:/restored" }));

    // Only the echo of the box's own commit is kept off the typed text; any other change of the host's value wins.
    expect(folderBox().value).toBe("E:/restored");
  });

  it("showsTheLoadedFolder_andSendsNothingOnTheNextShortcut_afterARefusedWriteAndAChangeOfTheHostsValue", async () => {
    const events = host({
      "output.set": () => {
        throw new Error("output.invalidFolder");
      },
    });
    await renderFiles();
    const box = folderBox();
    fireEvent.change(box, { target: { value: "Z:/nowhere" } });
    fireEvent.blur(box);
    await screen.findByText(/Could not change the output/);
    expect(box.value).toBe("Z:/nowhere");
    expect(calls("output", "set")).toHaveLength(1);

    // A configuration is loaded with its own folder.
    events.push("files.changed", withFile({ revision: 5, outputFolder: "E:/loaded", outputHint: "E:/loaded" }));
    expect(box.value).toBe("E:/loaded");

    // The run starts on what was loaded: the refused text is not sent over it.
    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });
    await waitFor(() => expect(calls("run", "start")).toHaveLength(1));
    expect(calls("output", "set")).toHaveLength(1);
    expect(box.value).toBe("E:/loaded");
  });
});
