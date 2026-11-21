import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, notRunResults, readyRun, standardSettings } from "../../../test/snapshots";
import type { RunSnapshot } from "../../../types/domain";
import Shell from "../Shell";

const readyCalls = () => vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "app" && method === "ready");
const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** A host whose run.get answers when `release` is called, so a test can look at the page before its data is there. */
function startHost(extra: Record<string, (params: unknown) => unknown> = {}) {
  let release: (run: RunSnapshot) => void = () => {};
  const runAnswer = new Promise<RunSnapshot>((resolve) => {
    release = resolve;
  });
  const host = mockBridge({
    "app.info": () => ({ version: "1.2.3" }),
    "run.get": () => runAnswer,
    "files.get": () => emptyFiles,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
    "app.ready": () => ({}),
    ...extra,
  });
  return { host, release };
}

describe("Shell tells the host the page is ready", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("callsAppReadyOnce_afterTheFirstSnapshotsAreShown", async () => {
    const { release } = startHost();
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);

    // The host keeps the window hidden until this call, so it must not come before there is something to show.
    await pause(100);
    expect(readyCalls()).toHaveLength(0);

    release(readyRun);
    await screen.findByText(/of 0 files/);

    await waitFor(() => expect(readyCalls()).toHaveLength(1), { timeout: 3000 });
    // The data had been painted when it was sent.
    expect(screen.getByText(/of 0 files/)).toBeTruthy();
  });

  it("doesNotCallAppReadyAgain_whenLaterSnapshotsArrive", async () => {
    const { host, release } = startHost();
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    release(readyRun);
    await waitFor(() => expect(readyCalls()).toHaveLength(1), { timeout: 3000 });

    host.push("run.changed", { ...readyRun, revision: 5, status: "Running", canStart: false, currentFile: "a.dll", total: 1 });
    host.push("files.changed", { ...emptyFiles, revision: 5 });
    await pause(100);

    expect(readyCalls()).toHaveLength(1);
  });

  it("swallowsAFailedAppReady_asTheHostShowsTheWindowOnItsOwnTimeout", async () => {
    const { release } = startHost({
      "app.ready": () => {
        throw new Error("host is gone");
      },
    });
    render(<Shell screen="files" onNavigate={vi.fn()}>content</Shell>);
    release(readyRun);

    await waitFor(() => expect(readyCalls()).toHaveLength(1), { timeout: 3000 });
    await pause(50);

    expect(screen.getByText(/of 0 files/)).toBeTruthy();
  });
});
