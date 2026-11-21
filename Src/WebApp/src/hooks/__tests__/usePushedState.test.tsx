import { act, render, renderHook, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { events } from "../../interop/api";
import { mockBridge } from "../../test/bridge";
import { idleRun, readyRun, standardSettings } from "../../test/snapshots";
import type { RunSnapshot } from "../../types/domain";
import { useRun } from "../useRun";
import { usePushedState } from "../usePushedState";

const running = (revision: number): RunSnapshot => ({ ...readyRun, revision, canStart: false, status: "Running", currentFile: "a.dll", total: 1 });

function RunBarProbe() {
  const { run } = useRun();
  return <p>{run ? `${run.status}@${run.revision}` : "unknown"}</p>;
}

describe("usePushedState", () => {
  it("dropsAPushOlderThanTheHeldSnapshot_soAStaleRunningCannotOutliveTheFinalState", async () => {
    const host = mockBridge({ "run.get": () => idleRun });
    render(<RunBarProbe />);
    await screen.findByText("Idle@1");

    // The final snapshot reaches the page first, then the 'Cancelling' push that was sent before it.
    host.push("run.changed", { ...readyRun, revision: 8, status: "Cancelled" });
    host.push("run.changed", running(7));

    expect(screen.getByText("Cancelled@8")).toBeTruthy();
  });

  it("keepsNewerPushesInOrder_whateverOrderTheyArriveIn", async () => {
    const host = mockBridge({ "run.get": () => idleRun });
    render(<RunBarProbe />);
    await screen.findByText("Idle@1");

    host.push("run.changed", running(4));
    host.push("run.changed", running(3));
    host.push("run.changed", running(6));
    host.push("run.changed", running(5));

    expect(screen.getByText("Running@6")).toBeTruthy();
  });

  it("appliesAPushWithTheSameRevision_asItIsTheSameSnapshot", async () => {
    const host = mockBridge({ "run.get": () => idleRun });
    const { result } = renderHook(() => usePushedState<RunSnapshot>("run", "get", "run.changed"));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    host.push("run.changed", running(5));
    host.push("run.changed", { ...running(5), progress: 50 });

    expect(result.current.state?.progress).toBe(50);
  });

  it("appliesAPushThatCarriesNoRevision_asItCannotBeShownToBeStale", async () => {
    const host = mockBridge({ "run.get": () => idleRun });
    const { result } = renderHook(() => usePushedState<RunSnapshot>("run", "get", events.runChanged));
    await waitFor(() => expect(result.current.state).not.toBeNull());

    // Every snapshot the host sends has a revision. The guard only drops what it can prove is older than the one held.
    host.push(events.runChanged, { ...running(5), revision: undefined });

    expect(result.current.state?.status).toBe("Running");
  });

  it("neverSubscribes_whenTheHostPushesNothingForTheState", async () => {
    const { apiClient } = await import("../../interop/WebViewApiClient");
    vi.mocked(apiClient.on).mockClear();
    mockBridge({ "app.info": () => ({ version: "1.0.0" }) });

    const { result } = renderHook(() => usePushedState<{ version: string }>("app", "info", null));
    await waitFor(() => expect(result.current.state?.version).toBe("1.0.0"));

    expect(vi.mocked(apiClient.on)).not.toHaveBeenCalled();
  });

  it("keepsTheSettingsSnapshotItHolds_whenAnOlderOneIsPushed", async () => {
    const host = mockBridge({ "settings.get": () => ({ ...standardSettings, revision: 3 }) });
    const { result } = renderHook(() => usePushedState<typeof standardSettings>("settings", "get", "settings.changed"));
    await waitFor(() => expect(result.current.state?.revision).toBe(3));

    act(() => host.push("settings.changed", { ...standardSettings, revision: 9, level: "Aggressive" }));
    act(() => host.push("settings.changed", { ...standardSettings, revision: 4, level: "Minimal" }));

    expect(result.current.state?.level).toBe("Aggressive");
  });
});
