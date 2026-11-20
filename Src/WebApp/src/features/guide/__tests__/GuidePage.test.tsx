import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import guide from "../../../content/guide.json";
import GuidePage from "../GuidePage";

function Harness({ initial = "start" }: { initial?: string }) {
  const [topic, setTopic] = useState(initial);
  return <GuidePage topic={topic} onTopicChange={setTopic} />;
}

describe("GuidePage", () => {
  it("listsEveryTopic_inTheNav", () => {
    render(<Harness />);

    const nav = screen.getByRole("navigation", { name: "Guide topics" });
    const names = within(nav)
      .getAllByRole("button")
      .map((button) => button.textContent);

    expect(names).toEqual([
      "Getting started",
      "Files and output",
      "Settings and levels",
      "Techniques",
      "Shortcuts",
      "Results",
    ]);
    expect(names).toHaveLength(guide.topics.length);
  });

  it("showsTheFirstTopic_andMarksItCurrent", () => {
    render(<Harness />);

    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
    const nav = screen.getByRole("navigation", { name: "Guide topics" });
    expect(within(nav).getByRole("button", { name: "Getting started" }).getAttribute("aria-current")).toBe("true");
    expect(within(nav).getByRole("button", { name: "Results" }).getAttribute("aria-current")).toBeNull();
  });

  it("showsTheArticle_ofTheSelectedTopic", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Files and output" }));

    expect(screen.getByRole("heading", { level: 2, name: "Files and output" })).toBeTruthy();
    expect(screen.getByRole("heading", { level: 3, name: "Where the output goes" })).toBeTruthy();
    expect(screen.queryByRole("heading", { level: 2, name: "Getting started" })).toBeNull();
    expect(screen.getByRole("button", { name: "Files and output" }).getAttribute("aria-current")).toBe("true");
  });

  it("reportsTheChosenTopic", () => {
    const onTopicChange = vi.fn();
    render(<GuidePage topic="start" onTopicChange={onTopicChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Techniques" }));

    expect(onTopicChange).toHaveBeenCalledWith("tech");
  });

  it("rendersTheShortcuts_asKeycaps", () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole("button", { name: "Shortcuts" }));

    const table = screen.getByRole("table");
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(8);
    const run = rows.find((row) => within(row).queryByText("Run"));
    expect(run).toBeTruthy();
    expect(Array.from(run!.querySelectorAll("kbd")).map((key) => key.textContent)).toEqual(["Ctrl", "Enter"]);
    for (const key of ["Esc", "F1", "Delete"]) {
      expect(within(table).getByText(key).tagName).toBe("KBD");
    }
    expect(within(table).getByText("Copy selected log lines or symbols")).toBeTruthy();
  });

  it("opensAtTheTopic_itWasGiven", () => {
    render(<Harness initial="tech" />);

    expect(screen.getByRole("heading", { level: 2, name: "Techniques" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Techniques" }).getAttribute("aria-current")).toBe("true");
    expect(screen.getByText("Obfuscation is not encryption")).toBeTruthy();
  });

  it("fallsBackToTheFirstTopic_whenTheIdIsUnknown", () => {
    render(<Harness initial="nope" />);

    expect(screen.getByRole("heading", { level: 2, name: "Getting started" })).toBeTruthy();
  });

  it("rendersInlineMarkup_asBoldMonoAndKeycaps", () => {
    render(<Harness />);

    const article = screen.getByRole("region", { name: "Getting started" });
    expect(within(article).getByText("Obfuscate").tagName).toBe("B");
    expect(within(article).getByText("Enter").tagName).toBe("KBD");

    fireEvent.click(screen.getByRole("button", { name: "Files and output" }));
    expect(screen.getByText(".dll").className).toContain("mono");
  });
});
