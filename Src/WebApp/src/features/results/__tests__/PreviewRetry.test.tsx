import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiTimeoutError } from "../../../interop/WebViewApiClient";
import type { PreviewResult } from "../../../types/domain";
import PreviewTab from "../PreviewTab";

const paths = ["C:/out/MyApp.dll"];
const decompiled = (text: string): PreviewResult => ({ path: paths[0], text, error: null });

describe("PreviewTab", () => {
  it("offersToTryAgain_whenTheDecompileDidNotAnswer_andShowsTheTextOnceItDoes", async () => {
    const getPreview = vi
      .fn<(path: string) => Promise<PreviewResult>>()
      .mockRejectedValueOnce(new ApiTimeoutError("preview", "get"))
      .mockResolvedValueOnce(decompiled("class MyApp {}"));
    render(<PreviewTab paths={paths} selectedPath={paths[0]} getPreview={getPreview} />);

    // Choosing the same assembly again changes nothing, so the error must carry its own way out.
    expect((await screen.findByRole("alert")).textContent).toContain("did not answer in time");
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("class MyApp {}")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(getPreview).toHaveBeenCalledTimes(2);
    expect(getPreview).toHaveBeenLastCalledWith(paths[0]);
  });

  it("offersToTryAgain_whenTheHostReportsAnError", async () => {
    const getPreview = vi
      .fn<(path: string) => Promise<PreviewResult>>()
      .mockResolvedValueOnce({ path: paths[0], text: "", error: "The assembly could not be read." })
      .mockResolvedValueOnce(decompiled("class MyApp {}"));
    render(<PreviewTab paths={paths} selectedPath={paths[0]} getPreview={getPreview} />);

    expect((await screen.findByRole("alert")).textContent).toContain("could not be read");
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByText("class MyApp {}")).toBeTruthy();
  });

  it("saysItIsDecompiling_whileTheHostWorks_andDropsTheNoticeWhenItIsDone", async () => {
    let finish!: (result: PreviewResult) => void;
    const getPreview = vi.fn(() => new Promise<PreviewResult>((resolve) => (finish = resolve)));
    render(<PreviewTab paths={paths} selectedPath={paths[0]} getPreview={getPreview} />);

    expect((await screen.findByRole("status")).textContent).toBe("Decompiling…");

    finish(decompiled("class MyApp {}"));

    expect(await screen.findByText("class MyApp {}")).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("hasNoTryAgain_whenTheDecompileWorked", async () => {
    const getPreview = vi.fn(() => Promise.resolve(decompiled("class MyApp {}")));
    render(<PreviewTab paths={paths} selectedPath={paths[0]} getPreview={getPreview} />);

    await screen.findByText("class MyApp {}");

    expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
  });
});
