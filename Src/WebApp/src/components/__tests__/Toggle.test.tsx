import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Toggle from "../Toggle";

describe("Toggle", () => {
  it("callsOnChangeWithTheNewValue_whenClicked", () => {
    const onChange = vi.fn();
    render(<Toggle label="String encryption" checked={false} onChange={onChange} />);

    fireEvent.click(screen.getByRole("switch", { name: "String encryption" }));

    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("reflectsCheckedState", () => {
    render(<Toggle label="Merge" checked onChange={vi.fn()} />);

    expect((screen.getByRole("switch", { name: "Merge" }) as HTMLInputElement).checked).toBe(true);
  });

  it("isDisabled_whenDisabled", () => {
    render(<Toggle label="Packing" checked={false} disabled onChange={vi.fn()} />);

    expect((screen.getByRole("switch", { name: "Packing" }) as HTMLInputElement).disabled).toBe(true);
  });
});
