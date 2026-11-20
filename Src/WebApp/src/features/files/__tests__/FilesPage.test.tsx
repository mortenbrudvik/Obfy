import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, idleRun, readyRun } from "../../../test/snapshots";
import type { FileRow, InputSnapshot, RunSnapshot } from "../../../types/domain";
import FilesPage from "../FilesPage";

const row = (over: Partial<FileRow> & { name: string }): FileRow => ({
  id: `id-${over.name}`, path: `C:/src/${over.name}`, origin: "Added", kind: "Assembly", size: 2048, sizeText: "2 KB",
  included: true, status: "Pending", skipReason: null, outputPath: null, error: null, ...over,
});

const withFiles = (files: FileRow[], over: Partial<InputSnapshot> = {}): InputSnapshot => ({
  ...emptyFiles, revision: 2, files, hasIncludedFiles: true, outputHint: "obfy-out next to App.dll", ...over,
});

function host(input: InputSnapshot, run: RunSnapshot = readyRun, extra: Record<string, (params: unknown) => unknown> = {}) {
  return mockBridge({
    "files.get": () => input,
    "run.get": () => run,
    "files.setIncluded": () => input,
    "files.remove": () => input,
    "files.clear": () => input,
    "files.pick": () => input,
    "output.setSymbolMap": () => input,
    "output.set": () => input,
    "output.pickFolder": () => input,
    ...extra,
  });
}

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

describe("FilesPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("showsTheDropZoneAndHowItWorks_whenThereAreNoFiles", async () => {
    host(emptyFiles, idleRun);

    render(<FilesPage />);

    expect(await screen.findByRole("heading", { name: "What do you want to protect?" })).toBeTruthy();
    expect(screen.getByRole("group", { name: "Drop zone" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add files…" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Open solution…" })).toBeTruthy();
    expect(screen.getByText("How it works")).toBeTruthy();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
    expect(screen.getByRole("button", { name: "Load a configuration (obfy.json)" })).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
  });

  it("picksFiles_fromTheDropZoneButtons", async () => {
    host(emptyFiles, idleRun);
    render(<FilesPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Open solution…" }));

    await waitFor(() => expect(calls("files", "pick")).toHaveLength(1));
  });

  it("rendersTheRows_withASkippedRowsReason", async () => {
    host(withFiles([
      row({ name: "App.dll" }),
      row({ name: "Lib.csproj", kind: "Project", origin: "Solution", included: false, status: "Skipped", skipReason: "Not built: no output found" }),
    ]));

    render(<FilesPage />);

    const table = await screen.findByRole("table");
    expect(within(table).getByText("App.dll")).toBeTruthy();
    expect(within(table).getAllByText("2 KB")).toHaveLength(2);
    expect(within(table).getByText("Not built: no output found")).toBeTruthy();
    expect(within(table).getByText("Solution")).toBeTruthy();
    expect(within(table).getByText("Project")).toBeTruthy();
  });

  it("disablesTheIncludeBox_ofAProjectRow", async () => {
    host(withFiles([
      row({ name: "App.dll" }),
      row({ name: "Lib.csproj", kind: "Project", included: false, status: "Skipped", skipReason: "Not built" }),
    ]));

    render(<FilesPage />);

    const project = await screen.findByRole("checkbox", { name: "Include Lib.csproj" });
    expect((project as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByRole("checkbox", { name: "Include App.dll" }) as HTMLInputElement).disabled).toBe(false);
  });

  it("callsSetIncludedWithTheRowId_whenAnAssemblyIsToggled", async () => {
    host(withFiles([row({ name: "App.dll", included: true })]));
    render(<FilesPage />);

    fireEvent.click(await screen.findByRole("checkbox", { name: "Include App.dll" }));

    await waitFor(() => expect(calls("files", "setIncluded")).toHaveLength(1));
    expect(calls("files", "setIncluded")[0][2]).toEqual({ id: "id-App.dll", included: false });
  });

  it("removesTheSelectedRow_onDelete", async () => {
    host(withFiles([row({ name: "App.dll" }), row({ name: "Lib.dll" })]));
    render(<FilesPage />);

    const lib = (await screen.findByText("Lib.dll")).closest("tr")!;
    fireEvent.click(lib);
    fireEvent.keyDown(lib, { key: "Delete" });

    await waitFor(() => expect(calls("files", "remove")).toHaveLength(1));
    expect(calls("files", "remove")[0][2]).toEqual({ id: "id-Lib.dll" });
  });

  it("showsTheHostsRunModeSentence", async () => {
    host(withFiles([row({ name: "App.dll" })]), { ...readyRun, modeExplanation: "One assembly is included. It is processed on its own." });

    render(<FilesPage />);

    expect(await screen.findByText("One assembly is included. It is processed on its own.")).toBeTruthy();
  });

  it("showsTheSymbolMapPathField_onlyWhileTheToggleIsOn", async () => {
    const off = withFiles([row({ name: "App.dll" })], { writeSymbolMap: false });
    const bridge = host(off);
    render(<FilesPage />);

    const toggle = await screen.findByRole("switch", { name: "Write a symbol map" });
    expect((toggle as HTMLInputElement).checked).toBe(false);
    expect(screen.queryByLabelText("Symbol map file")).toBeNull();

    fireEvent.click(toggle);
    await waitFor(() => expect(calls("output", "setSymbolMap")).toHaveLength(1));
    expect(calls("output", "setSymbolMap")[0][2]).toEqual({ enabled: true });

    bridge.push("files.changed", { ...off, revision: 3, writeSymbolMap: true, symbolMapPath: "map.json", resolvedSymbolMapPath: "C:/out/map.json" });
    expect(await screen.findByLabelText("Symbol map file")).toBeTruthy();
    expect((screen.getByLabelText("Symbol map file") as HTMLInputElement).value).toBe("map.json");
  });

  it("setsTheOutputFolder_whenTheBoxIsLeft", async () => {
    host(withFiles([row({ name: "App.dll" })]));
    render(<FilesPage />);

    const box = await screen.findByLabelText("Output folder");
    expect((box as HTMLInputElement).placeholder).toBe("obfy-out next to App.dll");
    fireEvent.change(box, { target: { value: "C:/out" } });
    fireEvent.blur(box);

    await waitFor(() => expect(calls("output", "set")).toHaveLength(1));
    expect(calls("output", "set")[0][2]).toEqual({ folder: "C:/out" });
  });

  it("disablesEditing_whileARunIsInProgress", async () => {
    host(withFiles([row({ name: "App.dll" })]), { ...readyRun, status: "Running" });
    render(<FilesPage />);

    const box = await screen.findByRole("checkbox", { name: "Include App.dll" });
    await waitFor(() => expect((box as HTMLInputElement).disabled).toBe(true));
    expect((screen.getByRole("button", { name: "Add files…" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Clear" }) as HTMLButtonElement).disabled).toBe(true);
  });
});
