import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { ApiTimeoutError, DEFAULT_TIMEOUT_MS } from "../WebViewApiClient";

/** A host that takes `ms` to answer every call, on the fake clock. */
function installSlowHost(ms: number, data: unknown) {
  window.chrome = {
    webview: {
      addEventListener: () => {},
      postMessageWithAdditionalObjects: () => {},
      hostObjects: {
        apibridge: {
          InvokeMethod: () => new Promise<string>((resolve) => setTimeout(() => resolve(JSON.stringify({ success: true, data })), ms)),
        },
      },
    },
  };
}

describe("api timeouts", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    delete window.chrome;
  });

  it("preview.get_waitsForAHostThatIsStillDecompilingALargeModule", async () => {
    // The host decompiles the whole module on every call; a big output takes far longer than an ordinary call may.
    installSlowHost(90_000, { path: "C:/out/Big.dll", text: "class Big {}", error: null });

    const result = api.preview.get("C:/out/Big.dll");
    await vi.advanceTimersByTimeAsync(90_000);

    await expect(result).resolves.toEqual({ path: "C:/out/Big.dll", text: "class Big {}", error: null });
  });

  it("anOrdinaryCall_stillGivesUpAfterTheDefaultTimeout", async () => {
    installSlowHost(90_000, {});

    const result = api.results.summary();
    const assertion = expect(result).rejects.toBeInstanceOf(ApiTimeoutError);
    await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS + 1);

    await assertion;
  });
});
