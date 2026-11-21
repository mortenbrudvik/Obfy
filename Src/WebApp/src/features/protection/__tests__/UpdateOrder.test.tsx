import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import type { SettingsSnapshot } from "../../../types/domain";
import { protectionSnapshot } from "./fixtures";
import { useProtection } from "../useProtection";

type Patch = Record<string, unknown>;

/** A host whose settings.update answers when the test says so, one call at a time, and records what it was sent and when. */
function slowHost() {
  const sent: Patch[] = [];
  const waiting: { patch: Patch; answer: (snapshot: SettingsSnapshot | Error) => void }[] = [];
  mockBridge({
    "settings.get": () => protectionSnapshot({ revision: 1 }),
    "settings.update": (params) => {
      const patch = (params as { patch: Patch }).patch;
      sent.push(patch);
      return new Promise<SettingsSnapshot>((resolve, reject) => {
        waiting.push({ patch, answer: (result) => (result instanceof Error ? reject(result) : resolve(result)) });
      });
    },
  });
  /** Answers the oldest call the host has not answered yet. */
  const answerNext = (revision: number) => {
    const next = waiting.shift();
    if (!next) throw new Error("the host has no call waiting");
    act(() => next.answer(protectionSnapshot({ revision })));
  };
  const failNext = (message: string) => {
    const next = waiting.shift();
    if (!next) throw new Error("the host has no call waiting");
    act(() => next.answer(new Error(message)));
  };
  return { sent, answerNext, failNext };
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 10));

describe("useProtection.update", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockClear();
  });

  it("sendsOneUpdateAtATime_andOnlyTheLatestOfAFastRun_soTheHostEndsOnTheLastValue", async () => {
    const host = slowHost();
    const { result } = renderHook(() => useProtection());
    await waitFor(() => expect(result.current.settings).not.toBeNull(), { timeout: 3000 });

    // Typing 1, 12, 123 into a number box, or dragging a slider, issues a call per change.
    const first = result.current.update({ "stringEncryption.minLength": 1 });
    const second = result.current.update({ "stringEncryption.minLength": 12 });
    const third = result.current.update({ "stringEncryption.minLength": 123 });
    await tick();

    // The host takes each call on a thread of its own, so a second call while the first is out could overtake it.
    expect(host.sent).toEqual([{ "stringEncryption.minLength": 1 }]);

    host.answerNext(2);
    await waitFor(() => expect(host.sent).toHaveLength(2), { timeout: 3000 });
    expect(host.sent[1]).toEqual({ "stringEncryption.minLength": 123 });
    host.answerNext(3);

    await expect(Promise.all([first, second, third])).resolves.toEqual([true, true, true]);
    expect(host.sent).toHaveLength(2);
    await waitFor(() => expect(result.current.settings?.revision).toBe(3), { timeout: 3000 });
  });

  it("keepsTheOrderOfUpdatesToDifferentOptions_andDoesNotMergeThem", async () => {
    const host = slowHost();
    const { result } = renderHook(() => useProtection());
    await waitFor(() => expect(result.current.settings).not.toBeNull(), { timeout: 3000 });

    const a1 = result.current.update({ "a.x": 1 });
    const b = result.current.update({ "b.y": 2 });
    const a2 = result.current.update({ "a.x": 3 });
    await tick();
    host.answerNext(2);
    await waitFor(() => expect(host.sent).toHaveLength(2), { timeout: 3000 });
    host.answerNext(3);
    await waitFor(() => expect(host.sent).toHaveLength(3), { timeout: 3000 });
    host.answerNext(4);

    await Promise.all([a1, b, a2]);
    // One refused value must never take an unrelated option's change down with it, and the host sees them in order.
    expect(host.sent).toEqual([{ "a.x": 1 }, { "b.y": 2 }, { "a.x": 3 }]);
  });

  it("answersFalseForARefusedUpdate_andKeepsSendingTheOnesBehindIt", async () => {
    const host = slowHost();
    const { result } = renderHook(() => useProtection());
    await waitFor(() => expect(result.current.settings).not.toBeNull(), { timeout: 3000 });

    const refused = result.current.update({ "a.x": 1 });
    const next = result.current.update({ "b.y": 2 });
    await tick();
    host.failNext("settings.invalidValue");
    await waitFor(() => expect(host.sent).toHaveLength(2), { timeout: 3000 });
    host.answerNext(5);

    await expect(refused).resolves.toBe(false);
    await expect(next).resolves.toBe(true);
    await waitFor(() => expect(result.current.settings?.revision).toBe(5), { timeout: 3000 });
    expect(result.current.error).toBeNull();
  });

  it("keepsTheNewestSnapshot_whenALevelChangeAnswersAfterALaterUpdate", async () => {
    let releaseLevel!: (snapshot: SettingsSnapshot) => void;
    mockBridge({
      "settings.get": () => protectionSnapshot({ revision: 1 }),
      "settings.applyLevel": () => new Promise<SettingsSnapshot>((resolve) => (releaseLevel = resolve)),
      "settings.update": () => protectionSnapshot({ revision: 3, level: "Custom" }),
    });
    const { result } = renderHook(() => useProtection());
    await waitFor(() => expect(result.current.settings).not.toBeNull(), { timeout: 3000 });

    const level = result.current.applyLevel("Minimal");
    await result.current.update({ "a.x": 1 });
    await waitFor(() => expect(result.current.settings?.revision).toBe(3), { timeout: 3000 });
    act(() => releaseLevel(protectionSnapshot({ revision: 2, level: "Minimal" })));
    await level;

    // The older answer comes last and must not take the newer state away again.
    expect(result.current.settings?.revision).toBe(3);
    expect(result.current.settings?.level).toBe("Custom");
  });
});
