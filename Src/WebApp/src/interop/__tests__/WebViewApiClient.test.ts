import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WebViewApiClient } from "../WebViewApiClient";

type MessageListener = (event: MessageEvent<unknown>) => void;

describe("WebViewApiClient", () => {
  let listeners: MessageListener[];
  let invoke: ReturnType<typeof vi.fn<(service: string, method: string, json: string) => Promise<string>>>;

  beforeEach(() => {
    listeners = [];
    invoke = vi.fn();
    window.chrome = {
      webview: {
        addEventListener: (_type: string, listener: MessageListener) => listeners.push(listener),
        hostObjects: { apibridge: { InvokeMethod: invoke } },
      },
    };
  });

  afterEach(() => {
    delete window.chrome;
  });

  it("call_resolvesData_onSuccessEnvelope", async () => {
    invoke.mockResolvedValue(JSON.stringify({ success: true, data: { version: "1.2.3" } }));

    const result = await new WebViewApiClient().call<{ version: string }>("app", "info", { a: 1 });

    expect(result).toEqual({ version: "1.2.3" });
    expect(invoke).toHaveBeenCalledWith("app", "info", JSON.stringify({ a: 1 }));
  });

  it("call_throws_onErrorEnvelope", async () => {
    invoke.mockResolvedValue(JSON.stringify({ success: false, error: { code: "Internal", message: "It broke." } }));

    await expect(new WebViewApiClient().call("app", "info")).rejects.toThrow("It broke.");
  });

  it("on_receivesPushedEvent", () => {
    const client = new WebViewApiClient();
    const received: unknown[] = [];
    const off = client.on("app.thing", (data) => received.push(data));

    listeners.forEach((l) => l(new MessageEvent("message", { data: { eventName: "app.thing", data: { n: 1 } } })));
    off();
    listeners.forEach((l) => l(new MessageEvent("message", { data: { eventName: "app.thing", data: { n: 2 } } })));

    expect(received).toEqual([{ n: 1 }]);
  });
});
