import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../interop/WebViewApiClient";
import { mockBridge } from "../../test/bridge";
import ErrorBoundary from "../ErrorBoundary";

function Boom(): never {
  throw new Error("render failed");
}

const readyCalls = () => vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "app" && method === "ready");
const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

describe("ErrorBoundary", () => {
  beforeEach(() => {
    mockBridge({ "app.ready": () => ({}) });
    vi.mocked(apiClient.call).mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("showsAMessageWithAWayToReload_insteadOfABlankWindow_whenARenderThrows", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const onReload = vi.fn();

    render(
      <ErrorBoundary onReload={onReload}>
        <Boom />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("alert").textContent).toContain("Something went wrong");
    expect(screen.getByText(/restart Obfy/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Reload" }));
    expect(onReload).toHaveBeenCalledOnce();
  });

  it("logsTheErrorForTheDevTools", () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary onReload={vi.fn()}>
        <Boom />
      </ErrorBoundary>,
    );

    expect(log.mock.calls.flat().some((arg) => arg instanceof Error && arg.message === "render failed")).toBe(true);
  });

  it("rendersItsChildren_whenNothingThrows", async () => {
    render(
      <ErrorBoundary onReload={vi.fn()}>
        <p>fine</p>
      </ErrorBoundary>,
    );

    expect(screen.getByText("fine")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    // The page reveals the window itself once it has drawn: the boundary must not do it for a page that works.
    await pause(50);
    expect(readyCalls()).toHaveLength(0);
  });

  it("tellsTheHostThePageIsReadyOnce_whenItShowsTheCrashView_soTheWindowIsNotKeptHidden", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary onReload={vi.fn()}>
        <Boom />
      </ErrorBoundary>,
    );

    await waitFor(() => expect(readyCalls()).toHaveLength(1));
    await pause(50);
    expect(readyCalls()).toHaveLength(1);
  });

  it("stillShowsTheCrashView_whenTheHostRefusesAppReady", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    mockBridge({
      "app.ready": () => {
        throw new Error("host is gone");
      },
    });
    vi.mocked(apiClient.call).mockClear();

    render(
      <ErrorBoundary onReload={vi.fn()}>
        <Boom />
      </ErrorBoundary>,
    );

    await waitFor(() => expect(readyCalls()).toHaveLength(1));
    await pause(50);
    expect(screen.getByRole("alert").textContent).toContain("Something went wrong");
  });

  it("showsItsFallback_andDrawsTheChildrenAgainOnRetry_withoutTellingTheHost", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    let broken = true;
    function Sometimes() {
      if (broken) throw new Error("render failed");
      return <p>fixed</p>;
    }

    render(
      <ErrorBoundary
        fallback={(retry) => (
          <div role="alert">
            <button type="button" onClick={retry}>Try again</button>
          </div>
        )}
      >
        <Sometimes />
      </ErrorBoundary>,
    );
    expect(screen.getByRole("alert")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(screen.getByRole("alert")).toBeTruthy();

    broken = false;
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByText("fixed")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    // A fallback is a screen's, not the page's last resort: the window is revealed by the shell, not by it.
    await pause(50);
    expect(readyCalls()).toHaveLength(0);
  });
});
