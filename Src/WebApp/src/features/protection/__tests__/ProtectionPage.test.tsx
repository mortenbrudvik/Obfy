import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import type { SettingsSnapshot } from "../../../types/domain";
import ProtectionPage from "../ProtectionPage";
import { protectionSnapshot } from "./fixtures";

type Handlers = Record<string, (params: unknown) => unknown>;

function host(initial: SettingsSnapshot, extra: Handlers = {}) {
  return mockBridge({
    "settings.get": () => initial,
    "settings.update": () => initial,
    "settings.applyLevel": () => initial,
    "settings.openConfig": () => initial,
    "settings.saveConfig": () => initial,
    "settings.pickSigningKey": () => initial,
    ...extra,
  });
}

function calls(method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, m]) => service === "settings" && m === method);
}

const rowSwitch = (name: string) => screen.getByRole("switch", { name }) as HTMLInputElement;
const techniqueList = () => screen.getByRole("region", { name: "Techniques" });

describe("ProtectionPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("showsTheLevelCardsWithTheirMeters_andMarksTheCurrentLevel", async () => {
    host(protectionSnapshot({ level: "Standard" }));

    render(<ProtectionPage />);

    const standard = await screen.findByRole("radio", { name: /^Standard/ });
    expect(standard.getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("radio", { name: /^Minimal/ }).getAttribute("aria-checked")).toBe("false");
    expect(within(standard).getByText("Recommended")).toBeTruthy();
    expect(within(standard).getByText("Protection")).toBeTruthy();
    expect(within(standard).getByText("Runtime cost")).toBeTruthy();
    expect(within(standard).getByText("Compatibility risk")).toBeTruthy();
    // Custom has no meters: it is whatever the user makes of it.
    expect(within(screen.getByRole("radio", { name: /^Custom/ })).queryByText("Protection")).toBeNull();
  });

  it("appliesTheLevel_andShowsTheNewOnOffStates_whenALevelCardIsChosen", async () => {
    const aggressive = protectionSnapshot({ level: "Aggressive", revision: 2, dirty: true });
    host(protectionSnapshot({ level: "Standard" }), { "settings.applyLevel": () => aggressive });

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });
    expect(rowSwitch("Control flow").checked).toBe(false);

    fireEvent.click(screen.getByRole("radio", { name: /^Aggressive/ }));

    await waitFor(() => expect(calls("applyLevel")).toHaveLength(1));
    expect(calls("applyLevel")[0][2]).toEqual({ level: "Aggressive" });
    await waitFor(() => expect(rowSwitch("Control flow").checked).toBe(true));
    expect(rowSwitch("Reference proxy").checked).toBe(true);
    expect(screen.getByRole("radio", { name: /^Aggressive/ }).getAttribute("aria-checked")).toBe("true");
  });

  it("sendsAnUpdate_andFollowsTheHostToCustom_whenATechniqueIsSwitched", async () => {
    const custom = protectionSnapshot({ level: "Custom", revision: 2, dirty: true, on: ["controlFlow"], changed: ["controlFlow"] });
    host(protectionSnapshot({ level: "Standard" }), { "settings.update": () => custom });

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });

    fireEvent.click(rowSwitch("Control flow"));

    await waitFor(() => expect(calls("update")).toHaveLength(1));
    expect(calls("update")[0][2]).toEqual({ patch: { "controlFlow.enabled": true } });
    await waitFor(() => expect(screen.getByRole("radio", { name: /^Custom/ }).getAttribute("aria-checked")).toBe("true"));
    // The row that moved off the level says so.
    const row = rowSwitch("Control flow").closest("li") as HTMLElement;
    expect(within(row).getByText("Changed")).toBeTruthy();
  });

  it("sendsAnUpdateForAnOption_whenAnOptionOfTheSelectedTechniqueChanges", async () => {
    host(protectionSnapshot());

    render(<ProtectionPage />);
    fireEvent.click(await screen.findByRole("button", { name: /String encryption/ }));

    fireEvent.change(await screen.findByLabelText("Algorithm"), { target: { value: "Xor" } });

    await waitFor(() => expect(calls("update")).toHaveLength(1));
    expect(calls("update")[0][2]).toEqual({ patch: { "stringEncryption.algorithm": "Xor" } });
  });

  it("filtersTheRows_whenSomethingIsTypedInTheSearchBox", async () => {
    host(protectionSnapshot());

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });

    fireEvent.change(screen.getByRole("searchbox", { name: "Find an option" }), { target: { value: "string" } });

    expect(within(techniqueList()).getByRole("button", { name: /String encryption/ })).toBeTruthy();
    expect(screen.queryByRole("switch", { name: "Control flow" })).toBeNull();
    expect(screen.queryByRole("switch", { name: "Watermark" })).toBeNull();
    // A group label with no row left is not shown either.
    expect(within(techniqueList()).queryByText("Packaging")).toBeNull();
  });

  it("matchesTheSummaryToo_andSaysSoWhenNothingMatches", async () => {
    host(protectionSnapshot());

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });
    const find = screen.getByRole("searchbox", { name: "Find an option" });

    fireEvent.change(find, { target: { value: "debugger" } });
    expect(within(techniqueList()).getByRole("button", { name: /Debugger check/ })).toBeTruthy();

    fireEvent.change(find, { target: { value: "zzzz" } });
    expect(screen.getByText(/No option matches/)).toBeTruthy();
  });

  it("disablesTheRuntimeGatedTechniques_andSaysWhy_whenAPushedSnapshotRulesThemOut", async () => {
    const bridge = host(protectionSnapshot({ level: "Aggressive" }));

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Method body encryption" });
    expect(rowSwitch("Method body encryption").disabled).toBe(false);

    bridge.push("settings.changed", protectionSnapshot({ level: "Aggressive", revision: 3, runtimeProfile: "NativeAot" }));

    await waitFor(() => expect(rowSwitch("Method body encryption").disabled).toBe(true));
    for (const name of ["Method body encryption", "Memory image guard", "Virtualization", "Embed dependencies"]) {
      const row = rowSwitch(name).closest("li") as HTMLElement;
      expect(rowSwitch(name).disabled).toBe(true);
      expect(rowSwitch(name).checked).toBe(false);
      expect(within(row).getByText("Not available for NativeAOT")).toBeTruthy();
    }
    // A technique the profile does not rule out stays usable.
    expect(rowSwitch("Control flow").disabled).toBe(false);

    fireEvent.click(within(techniqueList()).getByRole("button", { name: /Method body encryption/ }));
    expect(screen.getByText(/It is switched off for you/)).toBeTruthy();
  });

  it("sendsTheRuntimeProfile_whenTheTargetRuntimeChanges", async () => {
    host(protectionSnapshot());

    render(<ProtectionPage />);
    const select = (await screen.findByLabelText("Target runtime")) as HTMLSelectElement;
    expect(select.value).toBe("Default");

    fireEvent.change(select, { target: { value: "NativeAot" } });

    await waitFor(() => expect(calls("update")).toHaveLength(1));
    expect(calls("update")[0][2]).toEqual({ patch: { runtimeProfile: "NativeAot" } });
  });

  it("showsTheHostsErrorOnTheIdField_whenTheWatermarkIsOnWithNoId", async () => {
    const message = "Watermark is enabled but no id was specified.";
    host(protectionSnapshot({ on: ["watermark"], errors: [{ field: "watermark.id", message }] }));

    render(<ProtectionPage />);
    fireEvent.click(await screen.findByRole("button", { name: /Watermark/ }));

    const id = await screen.findByLabelText(/^Id/);
    expect(id.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByText(message)).toBeTruthy();
    expect(id.getAttribute("aria-describedby")).toContain(screen.getByText(message).id);
  });

  it("showsNoIdError_whenTheHostReportsNone", async () => {
    host(protectionSnapshot({ on: ["watermark"], optionValues: { watermark: { id: "acme-1" } } }));

    render(<ProtectionPage />);
    fireEvent.click(await screen.findByRole("button", { name: /Watermark/ }));

    const id = (await screen.findByLabelText(/^Id/)) as HTMLInputElement;
    expect(id.value).toBe("acme-1");
    expect(id.getAttribute("aria-invalid")).toBeNull();
  });

  it("showsTheConfigurationChip_andSaysWhetherItIsSaved", async () => {
    const bridge = host(protectionSnapshot({ dirty: true, configPath: "C:/work/obfy.json" }));

    render(<ProtectionPage />);
    expect(await screen.findByText(/unsaved changes/)).toBeTruthy();
    expect(screen.getByText("obfy.json")).toBeTruthy();

    bridge.push("settings.changed", protectionSnapshot({ revision: 5, dirty: false, configPath: "C:/work/obfy.json" }));

    await waitFor(() => expect(screen.queryByText(/unsaved changes/)).toBeNull());
    expect(screen.getByText(/· saved/)).toBeTruthy();
  });

  it("opensAndSavesTheConfiguration_fromTheHeaderButtons", async () => {
    host(protectionSnapshot());

    render(<ProtectionPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Open…" }));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls("openConfig")).toHaveLength(1));
    await waitFor(() => expect(calls("saveConfig")).toHaveLength(1));
  });

  it("explainsAFailedUpdate_insteadOfLeavingTheControlLookingChanged", async () => {
    host(protectionSnapshot(), {
      "settings.update": () => {
        throw new Error("settings.invalidValue");
      },
    });

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });

    fireEvent.click(rowSwitch("Control flow"));

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(rowSwitch("Control flow").checked).toBe(false);
  });

  it("keepsTheNewestSnapshot_whenAnswersArriveOutOfOrder", async () => {
    const first = protectionSnapshot({ level: "Custom", revision: 2, on: ["controlFlow"] });
    const second = protectionSnapshot({ level: "Custom", revision: 3, on: ["controlFlow", "referenceProxy"] });
    let releaseFirst!: () => void;
    const firstAnswer = new Promise<void>((resolve) => (releaseFirst = resolve));
    let n = 0;
    host(protectionSnapshot(), {
      "settings.update": async () => {
        if (++n === 1) {
          await firstAnswer;
          return first;
        }
        return second;
      },
    });

    render(<ProtectionPage />);
    await screen.findByRole("switch", { name: "Control flow" });
    fireEvent.click(rowSwitch("Control flow"));
    fireEvent.click(rowSwitch("Reference proxy"));
    await waitFor(() => expect(rowSwitch("Reference proxy").checked).toBe(true));

    // The older answer comes last and must not take the proxy away again.
    releaseFirst();
    await waitFor(() => expect(calls("update")).toHaveLength(2));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(rowSwitch("Reference proxy").checked).toBe(true);
    expect(rowSwitch("Control flow").checked).toBe(true);
  });
});
