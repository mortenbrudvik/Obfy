import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { notRunResults } from "../../../test/snapshots";
import type { LogSnapshot } from "../../../types/domain";
import ResultsPage from "../ResultsPage";
import { filesOf, finishedResults, line, node, page, row, runningResults, runningRun } from "./fixtures";

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

const emptyLog: LogSnapshot = { revision: 1, lines: [] };

function host(over: Record<string, (params: unknown) => unknown> = {}) {
  return mockBridge({
    "results.summary": () => finishedResults,
    "files.get": () => filesOf(),
    "run.get": () => runningRun,
    "log.get": () => emptyLog,
    "results.symbols": () => page([]),
    ...over,
  });
}

describe("ResultsPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("showsNothingYet_beforeAnyRun", async () => {
    host({ "results.summary": () => notRunResults });

    render(<ResultsPage />);

    expect(await screen.findByText("Nothing to show yet. Results appear here after you obfuscate.")).toBeTruthy();
    expect(screen.queryByRole("tablist")).toBeNull();
  });

  it("listsEachFileWithItsStatus_andTheLiveLogTail_whileRunning", async () => {
    host({
      "results.summary": () => runningResults,
      "files.get": () =>
        filesOf(
          row({ name: "A.dll", status: "Success" }),
          row({ name: "B.dll", status: "Processing" }),
          row({ name: "C.dll", status: "Pending" }),
          row({ name: "Skipped.dll", status: "Pending", included: false }),
        ),
      "log.get": () => ({ revision: 3, lines: [line(1, "Info", "Loading A.dll"), line(2, "Warning", "Slow disk")] }),
    });

    render(<ResultsPage />);

    expect(await screen.findByRole("heading", { name: "Running" })).toBeTruthy();
    const items = within(await screen.findByRole("list", { name: "Files" })).getAllByRole("listitem");
    expect(items).toHaveLength(3);
    expect(within(items[0]).getByRole("img", { name: "Done" })).toBeTruthy();
    expect(within(items[1]).getByRole("img", { name: "In progress" })).toBeTruthy();
    expect(within(items[2]).getByRole("img", { name: "Waiting" })).toBeTruthy();
    expect(await screen.findByText("Loading A.dll")).toBeTruthy();
    expect(screen.getByText("Slow disk")).toBeTruthy();
  });

  it("appendsPushedLogLines_toTheTail_andDropsOnesAlreadyHeld", async () => {
    const h = host({
      "results.summary": () => runningResults,
      "log.get": () => ({ revision: 2, lines: [line(1, "Info", "first")] }),
    });

    render(<ResultsPage />);
    await screen.findByText("first");
    h.push("log.appended", { revision: 4, lines: [line(1, "Info", "first"), line(2, "Info", "second"), line(3, "Error", "third")] });

    expect(await screen.findByText("third")).toBeTruthy();
    expect(screen.getAllByText("first")).toHaveLength(1);
    expect(screen.getByText("second")).toBeTruthy();
  });

  it("emptiesTheTail_onLogCleared_andIgnoresAStaleBatchAfterIt", async () => {
    const h = host({
      "results.summary": () => runningResults,
      "log.get": () => ({ revision: 2, lines: [line(1, "Info", "first")] }),
    });

    render(<ResultsPage />);
    await screen.findByText("first");
    h.push("log.cleared", { revision: 5, lines: [] });
    await waitFor(() => expect(screen.queryByText("first")).toBeNull());
    h.push("log.appended", { revision: 4, lines: [line(2, "Info", "stale")] });
    h.push("log.appended", { revision: 6, lines: [line(3, "Info", "after")] });

    expect(await screen.findByText("after")).toBeTruthy();
    expect(screen.queryByText("stale")).toBeNull();
  });

  it("showsTheFinishedHeader_withFileCountAndElapsedTime", async () => {
    host({
      "files.get": () =>
        filesOf(row({ name: "A.dll", outputPath: "C:/out/A.dll" }), row({ name: "B.dll", outputPath: "C:/out/B.dll" })),
    });

    render(<ResultsPage />);

    expect(await screen.findByRole("heading", { name: "Finished" })).toBeTruthy();
    expect(await screen.findByText("2 files · 4.2 s")).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Summary" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("button", { name: "Open output folder" })).toBeTruthy();
  });

  it("hidesZeroCounters_andShowsTheTilesThatAreInTheSnapshot", async () => {
    host({
      "results.summary": () => ({
        ...finishedResults,
        groups: [
          { name: "Renamed", chips: [{ label: "Types", value: 128 }] },
          { name: "Metadata", chips: [{ label: "Metadata removed", value: 57 }] },
        ],
      }),
    });

    render(<ResultsPage />);

    expect(await screen.findByText("128")).toBeTruthy();
    expect(screen.getByText("Types")).toBeTruthy();
    expect(screen.getByText("57")).toBeTruthy();
    // The host sends no group and no tile for a zero counter, so the page has none either.
    expect(screen.queryByText("Encrypted")).toBeNull();
    expect(screen.queryByText("Protection")).toBeNull();
    expect(screen.queryByText("0")).toBeNull();
  });

  it("listsEachOutputFileWithItsPath_andRevealsOne", async () => {
    host({
      "files.get": () =>
        filesOf(
          row({ name: "A.dll", outputPath: "C:/out/A.dll" }),
          row({ name: "Bad.dll", status: "Error", error: "Not a .NET assembly" }),
        ),
      "files.reveal": () => filesOf(),
    });

    render(<ResultsPage />);

    expect(await screen.findByText("C:/out/A.dll")).toBeTruthy();
    expect(screen.getByText("Not a .NET assembly")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Reveal A.dll" }));
    await waitFor(() => expect(calls("files", "reveal")).toHaveLength(1));
    expect(calls("files", "reveal")[0][2]).toEqual({ id: "id-A.dll" });
  });

  it("exportsTheReportAndTheSymbolMap_andOpensTheOutputFolder", async () => {
    host({
      "results.exportReport": () => ({ exported: true, path: "C:/r.html" }),
      "results.exportMap": () => ({ exported: false, path: null }),
      "files.openOutput": () => filesOf(),
    });

    render(<ResultsPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Export report…" }));
    fireEvent.click(screen.getByRole("button", { name: "Export symbol map…" }));
    fireEvent.click(screen.getByRole("button", { name: "Open output folder" }));

    await waitFor(() => expect(calls("results", "exportReport")).toHaveLength(1));
    await waitFor(() => expect(calls("results", "exportMap")).toHaveLength(1));
    await waitFor(() => expect(calls("files", "openOutput")).toHaveLength(1));
    expect(await screen.findByText("Saved to C:/r.html")).toBeTruthy();
  });

  it("disablesTheExports_thatHaveNothingToExport", async () => {
    host({ "results.summary": () => ({ ...finishedResults, hasReport: false, symbolCount: 0 }) });

    render(<ResultsPage />);

    expect(((await screen.findByRole("button", { name: "Export report…" })) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Export symbol map…" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("doesNotRefetchSymbols_whenTheRevisionRisesWithoutAReload", async () => {
    const h = host({ "results.symbols": () => page([node({ id: "n1", originalName: "Foo", obfuscatedName: "a" })]) });

    render(<ResultsPage />);
    fireEvent.click(await screen.findByRole("tab", { name: "Symbols" }));
    await screen.findByText("Foo");
    const before = calls("results", "symbols").length;

    // The report arrived: a new revision, the same state and the same symbols.
    h.push("results.changed", { ...finishedResults, revision: 9 });
    await screen.findByText("Foo");

    expect(calls("results", "symbols")).toHaveLength(before);
  });

  it("refetchesSymbols_whenANewRunReloadsTheResults", async () => {
    const h = host({ "results.symbols": () => page([node({ id: "n1", originalName: "Foo", obfuscatedName: "a" })]) });

    render(<ResultsPage />);
    fireEvent.click(await screen.findByRole("tab", { name: "Symbols" }));
    await screen.findByText("Foo");
    const before = calls("results", "symbols").length;

    h.push("results.changed", { ...finishedResults, revision: 10, state: "Running" });
    await screen.findByRole("heading", { name: "Running" });
    h.push("results.changed", { ...finishedResults, revision: 11 });
    fireEvent.click(await screen.findByRole("tab", { name: "Symbols" }));

    await waitFor(() => expect(calls("results", "symbols").length).toBeGreaterThan(before));
  });

  it("decompilesTheChosenAssembly_andShowsAFailureAsAMessage", async () => {
    host({
      "preview.get": (params) => {
        const path = (params as { path: string }).path;
        if (path.endsWith("Core.dll")) throw new Error("That file is not one of this run's output assemblies.");
        return { path, text: "class MyApp {}", error: null };
      },
    });

    render(<ResultsPage />);
    fireEvent.click(await screen.findByRole("tab", { name: "Preview" }));
    // Nothing is decompiled until an assembly is chosen.
    expect(calls("preview", "get")).toHaveLength(0);

    fireEvent.change(screen.getByRole("combobox", { name: "Output assembly" }), { target: { value: "C:/out/MyApp.dll" } });
    expect(await screen.findByText("class MyApp {}")).toBeTruthy();
    expect(screen.getByRole("region", { name: /MyApp\.dll/ })).toBeTruthy();

    fireEvent.change(screen.getByRole("combobox", { name: "Output assembly" }), { target: { value: "C:/out/MyApp.Core.dll" } });
    expect((await screen.findByRole("alert")).textContent).toContain("not one of this run's output assemblies");
    expect(screen.queryByText("class MyApp {}")).toBeNull();
  });
});
