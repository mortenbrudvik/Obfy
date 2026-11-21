import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { mockBridge } from "../../../test/bridge";
import { emptyFiles, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import Shell from "../Shell";

const filesDrag = () => ({ dataTransfer: { types: ["Files"], files: [], dropEffect: "none" } });

async function renderShell() {
  mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "run.get": () => readyRun,
    "files.get": () => emptyFiles,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
  });
  const { container } = render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
  await screen.findByText(/of 0 files/);
  // Only what the drag starts is on the fake clock: the page has loaded on the real one.
  vi.useFakeTimers();
  return container.querySelector(".shell")!;
}

const overlay = () => screen.queryByTestId("drop-overlay");

describe("the drop overlay", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("goesAway_whenTheDragLeavesWithoutADragLeave_asWhenTheElementUnderTheCursorWasRemoved", async () => {
    const shell = await renderShell();

    fireEvent.dragEnter(shell, filesDrag());
    expect(overlay()).toBeTruthy();

    // A drag over the page fires dragover again and again; when they stop, the drag is over.
    act(() => {
      vi.advanceTimersByTime(3000);
    });

    expect(overlay()).toBeNull();
  });

  it("staysWhileDragoverKeepsComing", async () => {
    const shell = await renderShell();
    fireEvent.dragEnter(shell, filesDrag());

    for (let i = 0; i < 5; i++) {
      act(() => {
        vi.advanceTimersByTime(500);
      });
      fireEvent.dragOver(shell, filesDrag());
    }

    expect(overlay()).toBeTruthy();
  });

  it("showsAgain_whenAnUnbalancedLeaveHidItWhileTheDragIsStillOverThePage", async () => {
    const shell = await renderShell();
    fireEvent.dragEnter(shell, filesDrag());
    fireEvent.dragLeave(shell, filesDrag());
    fireEvent.dragLeave(shell, filesDrag());
    expect(overlay()).toBeNull();

    fireEvent.dragOver(shell, filesDrag());

    expect(overlay()).toBeTruthy();
  });

  it("countsEntersAndLeavesFromZeroAgain_afterItWasReset", async () => {
    const shell = await renderShell();
    fireEvent.dragEnter(shell, filesDrag());
    fireEvent.dragEnter(shell, filesDrag());
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    expect(overlay()).toBeNull();

    // A stale count would need two leaves to hide it again.
    fireEvent.dragEnter(shell, filesDrag());
    fireEvent.dragLeave(shell, filesDrag());

    expect(overlay()).toBeNull();
  });

  it("goesAway_whenADragEndsOrTheWindowLosesFocus", async () => {
    const shell = await renderShell();

    fireEvent.dragEnter(shell, filesDrag());
    fireEvent(window, new Event("dragend"));
    expect(overlay()).toBeNull();

    fireEvent.dragEnter(shell, filesDrag());
    fireEvent.blur(window);
    expect(overlay()).toBeNull();
  });
});
