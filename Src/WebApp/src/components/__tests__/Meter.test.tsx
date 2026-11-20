import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Meter from "../Meter";
import Badge from "../Badge";
import Notice from "../Notice";

describe("Meter", () => {
  it("describesItsValueOutOfThree", () => {
    render(<Meter label="Protection" value={2} />);

    expect(screen.getByRole("img", { name: "2 of 3" })).toBeTruthy();
    expect(screen.getByText("Protection")).toBeTruthy();
  });
});

describe("Badge", () => {
  it("rendersItsText", () => {
    render(<Badge tone="warn">Windows only</Badge>);

    expect(screen.getByText("Windows only").className).toContain("warn");
  });
});

describe("Notice", () => {
  it("rendersATitleAndMessage_asAnAlertWhenItIsAWarning", () => {
    render(<Notice tone="warn" title="Obfuscation is not encryption">Never embed secrets.</Notice>);

    expect(screen.getByText("Obfuscation is not encryption")).toBeTruthy();
    expect(screen.getByText("Never embed secrets.")).toBeTruthy();
  });
});
