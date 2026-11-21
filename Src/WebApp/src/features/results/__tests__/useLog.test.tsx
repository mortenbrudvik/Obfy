import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { mockBridge } from "../../../test/bridge";
import type { LogSnapshot } from "../../../types/domain";
import { line } from "./fixtures";
import { useLog } from "../useLog";

const messages = (lines: readonly { message: string }[]) => lines.map((l) => l.message);

describe("useLog", () => {
  it("keepsTheLinesOfABatchThatArrivesAfterANewerOne", async () => {
    const host = mockBridge({ "log.get": (): LogSnapshot => ({ revision: 2, lines: [line(1, "Info", "a"), line(2, "Info", "b")] }) });
    const { result } = renderHook(() => useLog());
    await waitFor(() => expect(result.current.lines).toHaveLength(2), { timeout: 3000 });

    host.push("log.appended", { revision: 6, lines: [line(5, "Info", "e"), line(6, "Info", "f")] });
    host.push("log.appended", { revision: 4, lines: [line(3, "Info", "c"), line(4, "Info", "d")] });

    expect(messages(result.current.lines)).toEqual(["a", "b", "c", "d", "e", "f"]);
  });

  it("showsWhy_whenClearingTheLogFails_insteadOfAnUnhandledRejection", async () => {
    mockBridge({
      "log.get": (): LogSnapshot => ({ revision: 2, lines: [line(1, "Info", "a")] }),
      "log.clear": () => {
        throw new Error("The log is busy.");
      },
    });
    const { result } = renderHook(() => useLog());
    await waitFor(() => expect(result.current.lines).toHaveLength(1), { timeout: 3000 });

    await act(async () => {
      await result.current.clear();
    });

    expect(result.current.error).toBe("Could not clear the log (The log is busy).");
    expect(messages(result.current.lines)).toEqual(["a"]);
  });

  it("dropsTheClearErrorAtTheNextClearThatWorks", async () => {
    let fail = true;
    mockBridge({
      "log.get": (): LogSnapshot => ({ revision: 2, lines: [line(1, "Info", "a")] }),
      "log.clear": (): LogSnapshot => {
        if (fail) throw new Error("The log is busy.");
        return { revision: 3, lines: [] };
      },
    });
    const { result } = renderHook(() => useLog());
    await waitFor(() => expect(result.current.lines).toHaveLength(1), { timeout: 3000 });
    await act(async () => result.current.clear());
    expect(result.current.error).not.toBeNull();

    fail = false;
    await act(async () => result.current.clear());

    expect(result.current.error).toBeNull();
    expect(result.current.lines).toEqual([]);
  });

  it("countsTheLinesItTrimmed_whenTheLogGrowsPastItsBound", async () => {
    const lines = Array.from({ length: 5100 }, (_, i) => line(i + 1, "Info", `l${i + 1}`));
    mockBridge({ "log.get": (): LogSnapshot => ({ revision: 5100, lines }) });

    const { result } = renderHook(() => useLog());
    await waitFor(() => expect(result.current.lines.length).toBeGreaterThan(0), { timeout: 3000 });

    expect(result.current.lines).toHaveLength(5000);
    expect(result.current.trimmed).toBe(100);
    expect(result.current.lines[0].message).toBe("l101");
  });
});
