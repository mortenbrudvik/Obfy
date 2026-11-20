import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import App from "../App";
import { mockBridge } from "../test/bridge";

describe("App", () => {
  it("rendersVersion_fromAppInfo", async () => {
    mockBridge({ "app.info": () => ({ version: "9.9.9" }) });

    render(<App />);

    expect(await screen.findByText(/Version 9\.9\.9/)).toBeTruthy();
  });

  it("updatesVersion_onPushedEvent", async () => {
    const bridge = mockBridge({ "app.info": () => ({ version: "1.0.0" }) });

    render(<App />);
    await screen.findByText(/Version 1\.0\.0/);
    bridge.push("app.infoChanged", { version: "2.0.0" });

    expect(await screen.findByText(/Version 2\.0\.0/)).toBeTruthy();
  });
});
