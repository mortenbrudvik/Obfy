import { beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { apiClient } from "../WebViewApiClient";

vi.mock("../WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

describe("api.app.openGuide", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset().mockResolvedValue({});
  });

  it("openGuide_sendsTheTopicId_toAppOpenGuide", async () => {
    await api.app.openGuide("keys");

    expect(apiClient.call).toHaveBeenCalledWith("app", "openGuide", { topic: "keys" }, undefined);
  });

  it("openGuide_withoutATopic_sendsNull_soTheHostOpensTheGuideStart", async () => {
    await api.app.openGuide();

    expect(apiClient.call).toHaveBeenCalledWith("app", "openGuide", { topic: null }, undefined);
  });

  it("openGuide_rejectsWhenTheHostFails", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("The guide could not be opened in your browser."));

    await expect(api.app.openGuide("files")).rejects.toThrow("could not be opened");
  });
});
