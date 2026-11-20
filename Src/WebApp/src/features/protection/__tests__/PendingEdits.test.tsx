import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import { emptyFiles, idleRun, notRunResults } from "../../../test/snapshots";
import type { RunSnapshot } from "../../../types/domain";
import Shell from "../../shell/Shell";
import ProtectionPage from "../ProtectionPage";
import { protectionSnapshot } from "./fixtures";

const blocked: RunSnapshot = { ...idleRun, blockedReason: "Watermark is enabled but no id was specified." };
const withError = protectionSnapshot({
  on: ["watermark"],
  errors: [{ field: "watermark.id", message: "Watermark is enabled but no id was specified." }],
});
const withId = protectionSnapshot({ revision: 2, on: ["watermark"], dirty: true, optionValues: { watermark: { id: "acme-1" } } });

/** The host as the page sees it, with the order in which the calls that matter arrived. */
function host(handlers: Record<string, (params: unknown) => unknown>) {
  const order: string[] = [];
  mockBridge({
    "app.info": () => ({ version: "1.0.0" }),
    "files.get": () => emptyFiles,
    "results.summary": () => notRunResults,
    "run.get": () => blocked,
    "settings.get": () => withError,
    ...Object.fromEntries(
      Object.entries(handlers).map(([key, handler]) => [
        key,
        (params: unknown) => {
          order.push(key);
          return handler(params);
        },
      ]),
    ),
  });
  return order;
}

function calls(service: string, method: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([s, m]) => s === service && m === method);
}

/** Opens the watermark detail and types an id without leaving the box. */
async function typeWatermarkId(id: string) {
  render(
    <Shell screen="protection" onNavigate={vi.fn()}>
      <ProtectionPage />
    </Shell>,
  );
  fireEvent.click(await screen.findByRole("button", { name: /Watermark/ }));
  const box = await screen.findByLabelText(/^Id/);
  fireEvent.change(box, { target: { value: id } });
  return box;
}

const later = () => new Promise((resolve) => setTimeout(resolve, 20));

describe("a shortcut pressed while a box still holds text", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("sendsTheTypedWatermarkId_andOnlyThenStartsTheRun_onCtrlEnter", async () => {
    let release!: () => void;
    const answered = new Promise<void>((resolve) => (release = resolve));
    const order = host({
      "settings.update": async () => {
        await answered;
        return withId;
      },
      "run.start": () => ({ ...blocked, status: "Running" }),
    });
    const box = await typeWatermarkId("acme-1");

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    // The run was blocked on the empty id, yet the shortcut sends the id first.
    await waitFor(() => expect(calls("settings", "update")).toHaveLength(1));
    expect(calls("settings", "update")[0][2]).toEqual({ patch: { "watermark.id": "acme-1" } });
    // And it waits until the host has taken the id in.
    await later();
    expect(order).toEqual(["settings.update"]);

    release();

    await waitFor(() => expect(order).toEqual(["settings.update", "run.start"]));
    expect((box as HTMLInputElement).value).toBe("acme-1");
  });

  it("doesNotStartTheRun_whenTheHostRefusesTheTypedValue", async () => {
    const order = host({
      "settings.update": () => {
        throw new Error("settings.invalidValue");
      },
      "run.start": () => ({ ...blocked, status: "Running" }),
    });
    await typeWatermarkId("acme-1");

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await screen.findByRole("alert");
    await later();
    expect(order).toEqual(["settings.update"]);
  });

  it("stillDoesNothing_onCtrlEnter_whenNothingWasTypedAndTheRunIsBlocked", async () => {
    const order = host({ "settings.update": () => withId, "run.start": () => ({ ...blocked, status: "Running" }) });
    render(
      <Shell screen="protection" onNavigate={vi.fn()}>
        <ProtectionPage />
      </Shell>,
    );
    await screen.findByRole("button", { name: /Watermark/ });

    fireEvent.keyDown(window, { key: "Enter", ctrlKey: true });

    await later();
    expect(order).toEqual([]);
  });

  it("sendsTheTypedWatermarkId_andOnlyThenSavesTheConfiguration_onCtrlS", async () => {
    let release!: () => void;
    const answered = new Promise<void>((resolve) => (release = resolve));
    const order = host({
      "settings.update": async () => {
        await answered;
        return withId;
      },
      "settings.saveConfig": () => ({ ...withId, dirty: false, configPath: "C:/work/obfy.json" }),
    });
    await typeWatermarkId("acme-1");

    fireEvent.keyDown(window, { key: "s", ctrlKey: true });

    await waitFor(() => expect(calls("settings", "update")).toHaveLength(1));
    await later();
    expect(order).toEqual(["settings.update"]);

    release();

    await waitFor(() => expect(order).toEqual(["settings.update", "settings.saveConfig"]));
  });

  it("sendsTheTypedValueBeforeSaving_whenTheSaveButtonOfThePageIsUsed", async () => {
    const order = host({
      "settings.update": () => withId,
      "settings.saveConfig": () => ({ ...withId, dirty: false, configPath: "C:/work/obfy.json" }),
    });
    await typeWatermarkId("acme-1");

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(order).toEqual(["settings.update", "settings.saveConfig"]));
  });

  it("doesNotSaveAValueTheHostRefused_onCtrlS", async () => {
    const order = host({
      "settings.update": () => {
        throw new Error("settings.invalidValue");
      },
      "settings.saveConfig": () => withId,
    });
    await typeWatermarkId("acme-1");

    fireEvent.keyDown(window, { key: "s", ctrlKey: true });

    // The page says the value was refused, and the shortcut says it did not save.
    expect((await screen.findAllByRole("alert")).length).toBeGreaterThan(0);
    await later();
    expect(order).toEqual(["settings.update"]);
  });
});
