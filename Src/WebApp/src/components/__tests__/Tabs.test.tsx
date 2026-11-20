import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Tabs from "../Tabs";

const tabs = [
  { id: "summary", label: "Summary" },
  { id: "symbols", label: "Symbols" },
];

describe("Tabs", () => {
  it("marksTheSelectedTab_andSwitchesOnClick", () => {
    const onChange = vi.fn();
    render(<Tabs label="Results" tabs={tabs} selected="summary" onChange={onChange} />);

    expect(screen.getByRole("tab", { name: "Summary" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("tab", { name: "Symbols" }).getAttribute("aria-selected")).toBe("false");
    fireEvent.click(screen.getByRole("tab", { name: "Symbols" }));

    expect(onChange).toHaveBeenCalledWith("symbols");
  });

  it("movesWithTheArrowKeys", () => {
    const onChange = vi.fn();
    render(<Tabs label="Results" tabs={tabs} selected="summary" onChange={onChange} />);

    fireEvent.keyDown(screen.getByRole("tab", { name: "Summary" }), { key: "ArrowRight" });

    expect(onChange).toHaveBeenCalledWith("symbols");
  });
});
