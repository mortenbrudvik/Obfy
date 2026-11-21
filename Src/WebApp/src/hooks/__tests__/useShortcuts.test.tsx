import { render, screen } from "@testing-library/react";
import { useEffect, useLayoutEffect, useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { useShortcuts } from "../useShortcuts";

/**
 * A screen whose Esc handler exists only once something arrives from the host, as the Shell's cancel does once the run
 * snapshot says Running. The state arrives after mount through a promise, so React renders it like any update the host
 * pushes, and runs that commit's layout effects before its passive effects.
 *
 * The key is pressed from a layout effect of the very commit that shows "running": the earliest moment a user (or a
 * test that has just seen "running") can press it, and always before that commit's passive effects have run. On a busy
 * machine the passive effects really are that late, which made the Shell's Esc test fail now and then in CI.
 */
function Probe({ onCancel }: { onCancel: () => void }) {
  const [running, setRunning] = useState(false);
  useEffect(() => {
    void Promise.resolve().then(() => setRunning(true));
  }, []);
  useShortcuts({ cancel: running ? onCancel : undefined });
  useLayoutEffect(() => {
    if (running) window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true }));
  }, [running]);
  return <div>{running ? "running" : "waiting"}</div>;
}

describe("useShortcuts", () => {
  it("usesTheHandlersOfTheCommitThatShowsThem_notTheOnesBeforeIt", async () => {
    const onCancel = vi.fn();
    render(<Probe onCancel={onCancel} />);

    await screen.findByText("running");

    // A handler published from a passive effect is still the previous one (none) when the key arrives in the same
    // commit, so the Esc is dropped and nothing retries it.
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
