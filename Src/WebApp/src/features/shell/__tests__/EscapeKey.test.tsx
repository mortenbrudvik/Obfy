import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { FileRow, RunSnapshot } from "../../../types/domain";
import FilesPage from "../../files/FilesPage";
import Shell from "../Shell";

const row: FileRow = {
  id: "a", name: "a.dll", path: "C:/src/a.dll", origin: "Added", kind: "Assembly", size: 2048, sizeText: "2 KB",
  included: true, status: "Processing", skipReason: null, outputPath: null, error: null,
};
const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 1, progress: 10 };

function startHost() {
  mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "run.get": () => running,
    "run.cancel": () => running,
    "files.get": () => ({ ...emptyFiles, revision: 2, files: [row], hasIncludedFiles: true }),
    "files.reveal": () => ({ ...emptyFiles, revision: 2, files: [row] }),
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
  });
}

const cancelCalls = () => vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "run" && method === "cancel");
const later = () => new Promise((resolve) => setTimeout(resolve, 20));

describe("Esc while a run goes", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("onlyClosesTheRowMenu_whenOneIsOpen_andCancelsTheRunOnTheNextEsc", async () => {
    startHost();
    render(
      <Shell screen="files" onNavigate={vi.fn()}>
        <FilesPage />
      </Shell>,
    );
    const more = await screen.findByRole("button", { name: "More actions for a.dll" });
    await screen.findByText(/Processing a\.dll/);
    fireEvent.click(more);
    expect(screen.getByRole("menu")).toBeTruthy();

    fireEvent.keyDown(more, { key: "Escape" });

    expect(screen.queryByRole("menu")).toBeNull();
    await later();
    expect(cancelCalls()).toHaveLength(0);

    fireEvent.keyDown(document.body, { key: "Escape" });
    await waitFor(() => expect(cancelCalls()).toHaveLength(1), { timeout: 3000 });
  });

  it("doesNotCancelTheRun_whenEscIsPressedInASearchBox", async () => {
    startHost();
    render(
      <Shell screen="files" onNavigate={vi.fn()}>
        <input type="search" aria-label="Find" />
      </Shell>,
    );
    await screen.findByText(/Processing a\.dll/);

    // Esc clears a search box; it is not meant for the run.
    const notPrevented = fireEvent.keyDown(screen.getByLabelText("Find"), { key: "Escape" });

    expect(notPrevented).toBe(true);
    await later();
    expect(cancelCalls()).toHaveLength(0);
  });

  it("doesNotCancelTheRun_whenSomethingElseHandledTheEsc", async () => {
    startHost();
    render(
      <Shell screen="files" onNavigate={vi.fn()}>
        <div tabIndex={0} aria-label="Popup" onKeyDown={(event) => event.preventDefault()} />
      </Shell>,
    );
    await screen.findByText(/Processing a\.dll/);

    fireEvent.keyDown(screen.getByLabelText("Popup"), { key: "Escape" });

    await later();
    expect(cancelCalls()).toHaveLength(0);
  });

  it("stillCancelsTheRun_whenEscIsPressedAnywhereElse", async () => {
    startHost();
    render(
      <Shell screen="files" onNavigate={vi.fn()}>
        <button type="button">Somewhere</button>
      </Shell>,
    );
    await screen.findByText(/Processing a\.dll/);

    fireEvent.keyDown(screen.getByRole("button", { name: "Somewhere" }), { key: "Escape" });

    await waitFor(() => expect(cancelCalls()).toHaveLength(1), { timeout: 3000 });
  });
});
