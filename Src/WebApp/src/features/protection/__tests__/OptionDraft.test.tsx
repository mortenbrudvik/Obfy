import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, idleRun, notRunResults } from "../../../test/snapshots";
import Shell from "../../shell/Shell";
import ProtectionPage from "../ProtectionPage";
import { protectionSnapshot } from "./fixtures";

const empty = protectionSnapshot({ on: ["watermark"] });
const withId = (id: string, revision = 2) => protectionSnapshot({ revision, on: ["watermark"], dirty: true, optionValues: { watermark: { id } } });

function host(handlers: Record<string, (params: unknown) => unknown>) {
  return mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "files.get": () => emptyFiles,
    "results.summary": () => notRunResults,
    "run.get": () => idleRun,
    "settings.get": () => empty,
    ...handlers,
  });
}

const updates = () => vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === "settings" && m === "update");

async function openWatermark() {
  render(
    <Shell screen="protection" onNavigate={vi.fn()}>
      <ProtectionPage />
    </Shell>,
  );
  fireEvent.click(await screen.findByRole("button", { name: /Watermark/ }));
  return (await screen.findByLabelText(/^Id/)) as HTMLInputElement;
}

describe("an option box and the host's answer to what was typed in it", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("keepsTextTypedAfterEnter_whenTheHostEchoesTheCommittedWatermarkId", async () => {
    let release!: () => void;
    const answered = new Promise<void>((resolve) => (release = resolve));
    host({
      "settings.update": async () => {
        await answered;
        return withId("acme-1");
      },
    });
    const box = await openWatermark();
    fireEvent.change(box, { target: { value: "acme-1" } });
    fireEvent.keyDown(box, { key: "Enter" });
    await waitFor(() => expect(updates()).toHaveLength(1));

    // The box keeps focus after Enter, and the user goes on typing before the host has answered.
    fireEvent.change(box, { target: { value: "acme-12" } });
    release();

    // The page has applied the answer once the configuration shows as changed.
    await screen.findByText(/unsaved changes/);
    expect(box.value).toBe("acme-12");
  });

  it("keepsTextTypedAfterCtrlSFlushedTheId_whenTheHostEchoesIt", async () => {
    let release!: () => void;
    const answered = new Promise<void>((resolve) => (release = resolve));
    host({
      "settings.update": async () => {
        await answered;
        return withId("acme-1");
      },
      "settings.saveConfig": () => ({ ...withId("acme-1", 3), dirty: false, configPath: "C:/work/obfy.json" }),
    });
    const box = await openWatermark();
    fireEvent.change(box, { target: { value: "acme-1" } });
    fireEvent.keyDown(window, { key: "s", ctrlKey: true });
    await waitFor(() => expect(updates()).toHaveLength(1));

    fireEvent.change(box, { target: { value: "acme-12" } });
    release();

    await screen.findByText("obfy.json");
    expect(box.value).toBe("acme-12");
  });

  it("showsTheHostsId_whenItNormalisedWhatWasSent_andNothingWasTypedSince", async () => {
    host({ "settings.update": () => withId("acme-1") });
    const box = await openWatermark();

    fireEvent.change(box, { target: { value: "  acme-1  " } });
    fireEvent.blur(box);

    await waitFor(() => expect(box.value).toBe("acme-1"));
  });

  it("showsTheHostsId_whenItAnsweredWithTheOldOne_andNothingWasTypedSince", async () => {
    host({ "settings.update": () => ({ ...empty, revision: 2 }) });
    const box = await openWatermark();

    fireEvent.change(box, { target: { value: "ignored" } });
    fireEvent.blur(box);

    await waitFor(() => expect(updates()).toHaveLength(1));
    await waitFor(() => expect(box.value).toBe(""));
  });

  it("keepsWhatWasTyped_whenTheHostRefusesTheCall", async () => {
    host({
      "settings.update": () => {
        throw new Error("settings.invalidValue");
      },
    });
    const box = await openWatermark();

    fireEvent.change(box, { target: { value: "bad id" } });
    fireEvent.blur(box);

    await screen.findByRole("alert");
    expect(box.value).toBe("bad id");
  });

  it("followsTheHostsId_whenAConfigurationIsLoadedAndTheBoxHoldsNoUncommittedEdit", async () => {
    const events = host({});
    const box = await openWatermark();
    expect(box.value).toBe("");

    events.push("settings.changed", withId("from-config"));

    expect(box.value).toBe("from-config");
  });
});
