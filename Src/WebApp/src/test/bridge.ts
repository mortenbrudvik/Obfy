import { act } from "@testing-library/react";
import { vi } from "vitest";
import { apiClient } from "../interop/WebViewApiClient";

type Handler = (params: unknown) => unknown;
type Listener = (data: unknown) => void;

/**
 * Plays the .NET side for a test, on the client that the test file has replaced with
 * `vi.mock("…/interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }))`.
 *
 * Each call to `service.method` answers what `handlers["service.method"]` returns for its params (a handler that
 * throws rejects the call, as a failed handler does); a call with no handler rejects. `push` delivers an event to
 * every callback the page has subscribed to it and not yet unsubscribed, inside `act`.
 *
 * The mocks' earlier calls are kept: a test that counts calls clears them first (`vi.mocked(apiClient.call).mockClear()`).
 */
export function mockBridge(handlers: Record<string, Handler>): { push(eventName: string, data: unknown): void } {
  const listeners = new Map<string, Listener[]>();

  vi.mocked(apiClient.call).mockImplementation(async (service, method, params) => {
    const key = `${service}.${method}`;
    const handler = handlers[key];
    if (!handler) throw new Error(`No mock bridge handler for ${key}`);
    return handler(params);
  });

  vi.mocked(apiClient.on).mockImplementation((eventName, callback) => {
    const listener = callback as Listener;
    listeners.set(eventName, [...(listeners.get(eventName) ?? []), listener]);
    return () => {
      listeners.set(eventName, (listeners.get(eventName) ?? []).filter((l) => l !== listener));
    };
  });

  return {
    push(eventName, data) {
      const subscribed = listeners.get(eventName) ?? [];
      act(() => subscribed.forEach((listener) => listener(data)));
    },
  };
}
