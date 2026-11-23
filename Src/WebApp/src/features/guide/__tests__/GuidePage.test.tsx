import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../../interop/WebViewApiClient", () => ({ apiClient: { call: vi.fn(), on: vi.fn(() => vi.fn()) } }));

import guide from "../../../content/guide.json";
import { apiClient } from "../../../interop/WebViewApiClient";
import { mockBridge } from "../../../test/bridge";
import GuidePage from "../GuidePage";

function Harness({ initial = "start" }: { initial?: string }) {
  const [topic, setTopic] = useState(initial);
  return <GuidePage topic={topic} onTopicChange={setTopic} />;
}

describe("GuidePage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    mockBridge({ "app.openGuide": () => ({}) });
  });

  it("listsTheQuickStartTopics_inTheNav", () => {
    render(<Harness />);

    const nav = screen.getByRole("navigation", { name: "Guide topics" });
    const names = within(nav)
      .getAllByRole("button")
      .map((button) => button.textContent);

    expect(names).toEqual(["Getting started", "Shortcuts"]);
    expect(names).toHaveLength(guide.topics.length);
    expect(guide.topics.map((topic) => topic.id)).toEqual(["start", "keys"]);
  });

  it("showsTheFirstTopic_andMarksItCurrent", () => {
    render(<Harness />);

    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
    const nav = screen.getByRole("navigation", { name: "Guide topics" });
    expect(within(nav).getByRole("button", { name: "Getting started" }).getAttribute("aria-current")).toBe("true");
    expect(within(nav).getByRole("button", { name: "Shortcuts" }).getAttribute("aria-current")).toBeNull();
  });

  it("showsTheArticle_ofTheSelectedTopic", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Shortcuts" }));

    expect(screen.getByRole("heading", { level: 2, name: "Shortcuts" })).toBeTruthy();
    expect(screen.queryByRole("heading", { level: 2, name: "Getting started" })).toBeNull();
    expect(screen.getByRole("button", { name: "Shortcuts" }).getAttribute("aria-current")).toBe("true");
  });

  it("reportsTheChosenTopic", () => {
    const onTopicChange = vi.fn();
    render(<GuidePage topic="start" onTopicChange={onTopicChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Shortcuts" }));

    expect(onTopicChange).toHaveBeenCalledWith("keys");
  });

  it("rendersTheShortcuts_asKeycaps_withTheirConditions", () => {
    render(<Harness initial="keys" />);

    const table = screen.getByRole("table");
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(8);
    expect(within(table).getByText("Save configuration as")).toBeTruthy();
    const run = rows.find((row) => within(row).queryByText("Run"));
    expect(run).toBeTruthy();
    expect(Array.from(run!.querySelectorAll("kbd")).map((key) => key.textContent)).toEqual(["Ctrl", "Enter"]);
    for (const key of ["Esc", "F1", "Delete"]) {
      expect(within(table).getByText(key).tagName).toBe("KBD");
    }
    expect(within(table).getByText("Only during a run.")).toBeTruthy();
    expect(within(table).getByText("Click a row in the file list first. Not during a run.")).toBeTruthy();
    expect(within(table).getByText("Saves to the file you opened or last saved. Asks where the first time.")).toBeTruthy();
  });

  it("doesNotClaimAShortcutThatHasNoHandler_orThatWorksEverywhere", () => {
    render(<Harness initial="keys" />);

    expect(screen.queryByText(/Copy selected log lines or symbols/)).toBeNull();
    expect(screen.queryByText(/work anywhere in the window/)).toBeNull();
    // Ctrl+C is the browser's own copy; the Log tab's Copy button is the way to copy log lines.
    const note = screen.getByText(/copies selected text, as in any page/);
    expect(within(note).getByText("C").tagName).toBe("KBD");
  });

  it("opensAtTheTopic_itWasGiven", () => {
    render(<Harness initial="keys" />);

    expect(screen.getByRole("heading", { level: 2, name: "Shortcuts" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Shortcuts" }).getAttribute("aria-current")).toBe("true");
  });

  it("fallsBackToTheFirstTopic_whenTheIdIsUnknown", () => {
    render(<Harness initial="tech" />);

    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
  });

  it("keepsTheNotConfidentialityNote_inTheQuickStart", () => {
    render(<Harness />);

    expect(screen.getByText("Obfuscation is not confidentiality")).toBeTruthy();
    expect(screen.getByText(/Do not ship real secrets/)).toBeTruthy();
  });

  it("saysWhatTheQuickStartGetsRight_aboutResultsAndTheSymbolMap", () => {
    render(<Harness />);

    const article = screen.getByRole("region", { name: "Getting started" });
    expect(within(article).getByText(/When every file succeeds, Results opens/)).toBeTruthy();
    expect(within(article).getByText(/because it is off by default/)).toBeTruthy();
    expect(within(article).getByText(/Preview/, { selector: "b" })).toBeTruthy();
    expect(within(article).queryByText(/never changed/)).toBeNull();
  });

  it("saysTheAppWillNotWriteOverAnInput_notThatItDoesWhenTheInputsFolderIsChosen", () => {
    render(<Harness />);

    const article = screen.getByRole("region", { name: "Getting started" });
    expect(within(article).getByText(/will not write over an input file/)).toBeTruthy();
    expect(within(article).getByText(/the run bar says why/)).toBeTruthy();
    expect(within(article).queryByText(/unless you choose/)).toBeNull();
    expect(within(article).queryByText(/leaves your input files alone/)).toBeNull();
  });

  it("rendersInlineMarkup_asBoldMonoAndKeycaps", () => {
    render(<Harness />);

    const article = screen.getByRole("region", { name: "Getting started" });
    expect(within(article).getByText("Obfuscate").tagName).toBe("B");
    expect(within(article).getByText("Enter").tagName).toBe("KBD");
    expect(within(article).getByText("MyApp.obfuscated.dll").className).toContain("mono");
  });

  it("offersTheFullGuideOnline_onEveryTopic_withASentenceSayingItOpensTheBrowser", () => {
    render(<Harness />);

    expect(screen.getByRole("button", { name: "Open the full guide online" })).toBeTruthy();
    expect(screen.getByText(/It opens in your browser\./)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Shortcuts" }));

    expect(screen.getByRole("button", { name: "Open the full guide online" })).toBeTruthy();
  });

  it("opensTheOnlineGuide_throughTheHost_withoutATopicOrAUrl", async () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Open the full guide online" }));

    await waitFor(() => expect(vi.mocked(apiClient.call)).toHaveBeenCalledWith("app", "openGuide", { topic: null }, undefined));
    expect(vi.mocked(apiClient.call)).toHaveBeenCalledTimes(1);
  });

  it("explainsWhenTheBrowserCouldNotBeStarted_andKeepsTheQuickStartOpen", async () => {
    mockBridge({
      "app.openGuide": () => {
        throw new Error("The guide could not be opened in your browser. Visit https://brudvik.com/obfy/guide instead.");
      },
    });
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Open the full guide online" }));

    expect(await screen.findByText(/Could not open the online guide \(The guide could not be opened in your browser/)).toBeTruthy();
    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
  });
});
