import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import App from "../App";
import { apiClient } from "../interop/WebViewApiClient";
import { mockBridge } from "../test/bridge";
import { protectionSnapshot } from "../features/protection/__tests__/fixtures";
import { idleRun, emptyFiles, standardSettings, notRunResults } from "../test/snapshots";

function bridge() {
  return mockBridge({
    "app.info": () => ({ version: "9.9.9" }),
    "run.get": () => idleRun,
    "files.get": () => emptyFiles,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
  });
}

describe("App", () => {
  it("showsTheVersionInTheSidebar_withoutListeningForAnEventTheHostNeverPushes", async () => {
    bridge();
    vi.mocked(apiClient.on).mockClear();

    render(<App />);
    await screen.findByText("Obfy 9.9.9");

    // The host pushes no event for the app info; a subscription to one would be dead code the page keeps alive.
    expect(vi.mocked(apiClient.on).mock.calls.map(([eventName]) => eventName)).not.toContain("app.infoChanged");
  });

  it("startsOnFiles_andNavigatesBetweenScreens", async () => {
    bridge();

    render(<App />);
    expect(screen.getByRole("heading", { name: "Files" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Files" }).getAttribute("aria-current")).toBe("page");

    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));

    expect(screen.getByRole("heading", { name: "Protection" })).toBeTruthy();
    expect(screen.getByRole("button", { name: /^Protection/ }).getAttribute("aria-current")).toBe("page");
  });

  it("opensTheGuide_onF1", async () => {
    bridge();

    render(<App />);
    fireEvent.keyDown(window, { key: "F1" });

    expect(screen.getByRole("heading", { name: "Guide" })).toBeTruthy();
    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
  });

  it("opensTheGuideAtItsFirstTopic_fromTheSidebar_andKeepsTheTopicOnF1WhileOpen", async () => {
    bridge();

    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Guide/ }));
    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Shortcuts" }));
    fireEvent.keyDown(window, { key: "F1" });

    expect(screen.getByRole("heading", { level: 2, name: "Shortcuts" })).toBeTruthy();
  });

  it("opensTheOnlineGuide_inTheBrowser_fromAReadMoreLink_andStaysOnProtection", async () => {
    const openGuide = vi.fn(() => ({}));
    mockBridge({
      "app.info": () => ({ version: "9.9.9" }),
      "run.get": () => idleRun,
      "files.get": () => emptyFiles,
      "settings.get": () => protectionSnapshot({ level: "Standard" }),
      "results.summary": () => notRunResults,
      "app.openGuide": openGuide,
    });

    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Read more online" }));

    await waitFor(() => expect(openGuide).toHaveBeenCalledWith({ topic: "techniques" }));
    // The page never navigates to the in-app guide for it: the host opens the browser.
    expect(screen.getByRole("heading", { name: "Protection" })).toBeTruthy();
    expect(screen.queryByTestId("guide-page")).toBeNull();
  });

  it("opensTheOnlineGuide_fromTheQuickStartsButton", async () => {
    const openGuide = vi.fn(() => ({}));
    mockBridge({
      "app.info": () => ({ version: "9.9.9" }),
      "run.get": () => idleRun,
      "files.get": () => emptyFiles,
      "settings.get": () => standardSettings,
      "results.summary": () => notRunResults,
      "app.openGuide": openGuide,
    });

    render(<App />);
    fireEvent.keyDown(window, { key: "F1" });
    fireEvent.click(screen.getByRole("button", { name: "Open the full guide online" }));

    await waitFor(() => expect(openGuide).toHaveBeenCalledWith({ topic: null }));
    expect(screen.getByTestId("guide-page")).toBeTruthy();
  });

  it("showsARecoverableMessage_insteadOfABlankWindow_whenTheHostSendsSomethingThePageCannotDraw", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    mockBridge({
      "app.info": () => ({ version: "9.9.9" }),
      "run.get": () => idleRun,
      "files.get": () => ({ ...emptyFiles, files: null }),
      "settings.get": () => standardSettings,
      "results.summary": () => notRunResults,
    });

    render(<App />);

    expect((await screen.findByRole("alert")).textContent).toContain("Something went wrong");
    expect(screen.getByRole("button", { name: "Reload" })).toBeTruthy();
    vi.mocked(console.error).mockRestore();
  });
});
