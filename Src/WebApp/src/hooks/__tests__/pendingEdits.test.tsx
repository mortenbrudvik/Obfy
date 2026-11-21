import { render, screen } from "@testing-library/react";
import { useEffect, useLayoutEffect, useState } from "react";
import { describe, expect, it } from "vitest";
import { flushPendingEdits, usePendingEdit } from "../pendingEdits";

/**
 * A box whose text arrives after mount, flushed from a layout effect of the commit that shows it: the earliest moment a
 * shortcut can run after the text is on screen, and always before that commit's passive effects. The flush must send
 * the text on screen, not the text of the render before it.
 */
function Box({ sent }: { sent: string[] }) {
  const [draft, setDraft] = useState("");
  useEffect(() => {
    void Promise.resolve().then(() => setDraft("typed"));
  }, []);
  usePendingEdit(async () => {
    sent.push(draft);
    return draft ? "sent" : "idle";
  });
  useLayoutEffect(() => {
    if (draft) void flushPendingEdits();
  }, [draft]);
  return <div>{draft || "empty"}</div>;
}

describe("usePendingEdit", () => {
  it("flushesTheTextOfTheCommitThatShowsIt_notTheOneBefore", async () => {
    const sent: string[] = [];
    render(<Box sent={sent} />);

    await screen.findByText("typed");

    // A flush published from a passive effect is still the previous render's, closed over the empty text, so a shortcut
    // pressed right after typing would act on the old value (a stale watermark id in the output).
    expect(sent).toEqual(["typed"]);
  });
});
