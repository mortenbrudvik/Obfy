import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import ErrorBoundary from "../ErrorBoundary";

function Boom(): never {
  throw new Error("render failed");
}

describe("ErrorBoundary", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("showsAMessageWithAWayToReload_insteadOfABlankWindow_whenARenderThrows", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const onReload = vi.fn();

    render(
      <ErrorBoundary onReload={onReload}>
        <Boom />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("alert").textContent).toContain("Something went wrong");
    expect(screen.getByText(/restart Obfy/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Reload" }));
    expect(onReload).toHaveBeenCalledOnce();
  });

  it("logsTheErrorForTheDevTools", () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary onReload={vi.fn()}>
        <Boom />
      </ErrorBoundary>,
    );

    expect(log.mock.calls.flat().some((arg) => arg instanceof Error && arg.message === "render failed")).toBe(true);
  });

  it("rendersItsChildren_whenNothingThrows", () => {
    render(
      <ErrorBoundary onReload={vi.fn()}>
        <p>fine</p>
      </ErrorBoundary>,
    );

    expect(screen.getByText("fine")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
