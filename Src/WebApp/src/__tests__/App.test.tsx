import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import App from "../App";
import { apiClient } from "../interop/WebViewApiClient";
import { mockBridge } from "../test/bridge";
import { protectionSnapshot } from "../features/protection/__tests__/fixtures";
import { idleRun, emptyFiles, standardSettings, notRunResults, readyRun } from "../test/snapshots";
import type { RunSnapshot } from "../types/domain";

function bridge() {
  return mockBridge({
    "app.info": () => ({ version: "9.9.9" }),
    "run.get": () => idleRun,
    "files.get": () => emptyFiles,
    "settings.get": () => standardSettings,
    "results.summary": () => notRunResults,
  });
}

const readyCalls = () => vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "app" && method === "ready");
const cancelCalls = () => vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "run" && method === "cancel");

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

  it("showsARecoverableMessage_insteadOfABlankWindow_whenTheHostSendsSomethingTheShellCannotDraw", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    mockBridge({
      "app.info": () => ({ version: "9.9.9" }),
      "run.get": () => idleRun,
      "files.get": () => ({ ...emptyFiles, files: null }),
      "settings.get": () => standardSettings,
      "results.summary": () => notRunResults,
      "app.ready": () => ({}),
    });
    vi.mocked(apiClient.call).mockClear();

    render(<App />);

    // The shell's own failure is the last resort: the whole page is the crash view, not a screen's in-place one.
    expect((await screen.findByRole("alert")).textContent).toContain("Something went wrong");
    expect(screen.getByRole("button", { name: "Reload" })).toBeTruthy();
    expect(screen.queryByRole("complementary", { name: "Sections" })).toBeNull();
    // The window is still hidden at this point: the crash view reveals it at once instead of after the host's timeout.
    await waitFor(() => expect(readyCalls()).toHaveLength(1));
    vi.mocked(console.error).mockRestore();
  });
});

describe("a screen that cannot draw what the host sent", () => {
  /** A settings snapshot the Protection screen cannot draw (no technique list) and the shell does not read. */
  const undrawable = { ...standardSettings, techniques: null };

  /** The host answers `settings.get` with whatever `current.settings` holds, so a test can fix the data between visits. */
  function hostWithSettings(current: { settings: unknown }, run: RunSnapshot = idleRun, extra: Record<string, () => unknown> = {}) {
    return mockBridge({
      "app.info": () => ({ version: "9.9.9" }),
      "run.get": () => run,
      "files.get": () => emptyFiles,
      "settings.get": () => current.settings,
      "results.summary": () => notRunResults,
      "app.ready": () => ({}),
      ...extra,
    });
  }

  beforeEach(() => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.mocked(apiClient.call).mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("showsAnErrorInPlace_andKeepsTheSidebarAndRunBar_insteadOfReplacingTheWholeShell", async () => {
    hostWithSettings({ settings: undrawable });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("The Protection screen could not be shown");
    expect(within(alert).getByRole("button", { name: "Try again" })).toBeTruthy();
    // The frame is still there: the sidebar to leave by, the run bar to start or cancel from.
    expect(screen.getByRole("complementary", { name: "Sections" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Obfuscate" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Reload" })).toBeNull();
  });

  it("stillCancelsARunFromTheRunBar_whileTheScreenShowsItsError", async () => {
    const running: RunSnapshot = { ...readyRun, canStart: false, status: "Running", currentFile: "a.dll", total: 1 };
    hostWithSettings({ settings: undrawable }, running, { "run.cancel": () => running });
    render(<App />);
    await screen.findByText(/Processing a\.dll/);

    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(cancelCalls()).toHaveLength(1));
  });

  it("keepsTheShortcutsWorking_whileTheScreenShowsItsError", async () => {
    hostWithSettings({ settings: undrawable });
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");

    fireEvent.keyDown(window, { key: "F1" });

    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
    expect(screen.queryByText("The Protection screen could not be shown")).toBeNull();
  });

  it("clearsTheError_whenTheUserOpensAnotherScreenAndComesBack", async () => {
    const host = { settings: undrawable as unknown };
    hostWithSettings(host);
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");

    fireEvent.click(screen.getByRole("button", { name: "Files" }));
    expect(screen.getByRole("heading", { name: "Files" })).toBeTruthy();
    expect(screen.queryByText("The Protection screen could not be shown")).toBeNull();

    host.settings = standardSettings;
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));

    expect(await screen.findByRole("heading", { name: "Protection" })).toBeTruthy();
    expect(screen.queryByText("The Protection screen could not be shown")).toBeNull();
  });

  it("drawsTheScreenAgain_whenTheUserTriesAgainAndTheDataIsFine", async () => {
    const host = { settings: undrawable as unknown };
    hostWithSettings(host);
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");

    host.settings = standardSettings;
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("heading", { name: "Protection" })).toBeTruthy();
    expect(screen.queryByText("The Protection screen could not be shown")).toBeNull();
  });

  it("movesFocusToTheContentArea_whenTryAgainDrawsTheScreen_insteadOfLettingItFallToTheDocument", async () => {
    const host = { settings: undrawable as unknown };
    hostWithSettings(host);
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    const retry = await screen.findByRole("button", { name: "Try again" });
    retry.focus();
    expect(document.activeElement).toBe(retry);

    host.settings = standardSettings;
    fireEvent.click(retry);
    await screen.findByRole("heading", { name: "Protection" });

    // The button that had focus is gone; a keyboard or screen reader user lands on the screen, not at the top of the page.
    expect(document.activeElement).toBe(screen.getByRole("main"));
  });

  it("showsTheErrorAgain_whenTheUserTriesAgainAndTheDataIsStillBad", async () => {
    hostWithSettings({ settings: undrawable });
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("The Protection screen could not be shown")).toBeTruthy();
    expect(screen.getByRole("complementary", { name: "Sections" })).toBeTruthy();
  });

  it("doesNotCallAppReadyForIt_asTheShellHasAlreadyDoneThat", async () => {
    hostWithSettings({ settings: undrawable });
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: /^Protection/ }));
    await screen.findByText("The Protection screen could not be shown");
    await waitFor(() => expect(readyCalls()).toHaveLength(1));

    // Only the shell's own signal: the in-place view does not send a second one.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(readyCalls()).toHaveLength(1);
  });
});
