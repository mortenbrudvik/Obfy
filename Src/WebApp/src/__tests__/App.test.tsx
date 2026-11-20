import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import App from "../App";
import { mockBridge } from "../test/bridge";
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
  it("showsTheVersionInTheSidebar_andFollowsAPushedVersion", async () => {
    const host = bridge();

    render(<App />);
    await screen.findByText("Obfy 9.9.9");
    host.push("app.infoChanged", { version: "2.0.0" });

    expect(await screen.findByText("Obfy 2.0.0")).toBeTruthy();
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
  });
});
