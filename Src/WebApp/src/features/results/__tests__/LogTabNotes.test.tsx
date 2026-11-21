import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import LogTab from "../LogTab";
import { defaultLogOptions } from "../logFormat";
import { line } from "./fixtures";

const lines = [line(1, "Info", "first")];

describe("LogTab notes", () => {
  it("saysOlderLinesWereTrimmed_whenThePageLetSomeGo", () => {
    render(<LogTab lines={lines} options={defaultLogOptions} onOptionsChange={vi.fn()} onClear={vi.fn()} trimmed={1200} />);

    expect(screen.getByRole("status").textContent).toBe("Showing the latest 5,000 lines. 1,200 older lines were trimmed from this view.");
  });

  it("saysNothing_whenNothingWasTrimmed", () => {
    render(<LogTab lines={lines} options={defaultLogOptions} onOptionsChange={vi.fn()} onClear={vi.fn()} />);

    expect(screen.queryByRole("status")).toBeNull();
  });

  it("showsWhyTheLogCouldNotBeCleared", () => {
    render(<LogTab lines={lines} options={defaultLogOptions} onOptionsChange={vi.fn()} onClear={vi.fn()} clearError="Could not clear the log (The log is busy)." />);

    expect(screen.getByRole("alert").textContent).toBe("Could not clear the log (The log is busy).");
  });

  it("asksToClear_whenClearIsPressed", () => {
    const onClear = vi.fn();
    render(<LogTab lines={lines} options={defaultLogOptions} onOptionsChange={vi.fn()} onClear={onClear} />);

    fireEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(onClear).toHaveBeenCalledOnce();
  });
});
