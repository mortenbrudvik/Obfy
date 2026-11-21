import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { useDraft } from "../useDraft";

function setup(initial: string) {
  return renderHook(({ value }) => useDraft(value), { initialProps: { value: initial } });
}

/** A call the test answers when it chooses, as the host does. */
function pendingCall<T>() {
  let resolve!: (answer: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("useDraft", () => {
  it("showsTheHostsValue_andFollowsItWhileNothingIsTyped", () => {
    const { result, rerender } = setup("a");
    expect(result.current[0]).toBe("a");

    rerender({ value: "b" });

    expect(result.current[0]).toBe("b");
  });

  it("replacesWhatIsBeingTyped_whenTheHostChangesAndNoSendIsInFlight", () => {
    const { result, rerender } = setup("a");
    act(() => result.current[1]("typed"));

    // A configuration is loaded: what the host now holds is what the box shows.
    rerender({ value: "b" });

    expect(result.current[0]).toBe("b");
  });

  it("keepsTextTypedAfterASend_whenTheHostEchoesTheSentText", async () => {
    const { result, rerender } = setup("");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("C:\\out"));
    act(() => void result.current[2]("C:\\out", () => call.promise));
    act(() => result.current[1]("C:\\out2"));

    rerender({ value: "C:\\out" });
    await act(async () => call.resolve(true));

    expect(result.current[0]).toBe("C:\\out2");
  });

  it("showsTheHostsValue_whenItNormalisedTheSentText_andNothingWasTypedSince", async () => {
    const { result, rerender } = setup("");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("  acme  "));
    act(() => void result.current[2]("  acme  ", () => call.promise));

    rerender({ value: "acme" });
    await act(async () => call.resolve(true));

    expect(result.current[0]).toBe("acme");
  });

  it("showsTheHostsValue_whenItKeptTheOldOne_andNothingWasTypedSince", async () => {
    const { result } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("new"));
    act(() => void result.current[2]("new", () => call.promise));

    // The host answers, and its value is the one it had.
    await act(async () => call.resolve(true));

    expect(result.current[0]).toBe("old");
  });

  it("keepsTextTypedSince_whenTheHostKeptTheOldValue", async () => {
    const { result } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("new"));
    act(() => void result.current[2]("new", () => call.promise));
    act(() => result.current[1]("newer"));

    await act(async () => call.resolve(true));

    expect(result.current[0]).toBe("newer");
  });

  it("showsTheHostsValueTheAnswerCameWith_whenTheHostChangedInTheSameRenderTheCallSettles", async () => {
    // The caller applies the host's reply and the call settles in one batch: no step shows the value from before the reply.
    const { result, rerender } = setup("");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("  acme  "));
    act(() => void result.current[2]("  acme  ", () => call.promise));
    const shown: string[] = [];

    await act(async () => {
      rerender({ value: "acme" });
      call.resolve(true);
    });
    shown.push(result.current[0]);

    expect(shown).toEqual(["acme"]);
  });

  it("keepsTextTypedSince_whenTheHostChangedInTheSameRenderTheCallSettles", async () => {
    const { result, rerender } = setup("");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("acme"));
    act(() => void result.current[2]("acme", () => call.promise));
    act(() => result.current[1]("acme2"));

    await act(async () => {
      rerender({ value: "acme" });
      call.resolve(true);
    });

    expect(result.current[0]).toBe("acme2");
  });

  it("keepsWhatWasSent_whenTheCallIsRefused", async () => {
    const { result } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("bad"));
    act(() => void result.current[2]("bad", () => call.promise));

    await act(async () => call.resolve(false));

    expect(result.current[0]).toBe("bad");
  });

  it("keepsWhatWasSent_whenTheCallThrows", async () => {
    const { result } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("bad"));
    let outcome: unknown;
    act(() => {
      result.current[2]("bad", () => call.promise).catch((error: unknown) => (outcome = error));
    });

    await act(async () => call.reject(new Error("host is gone")));

    expect(result.current[0]).toBe("bad");
    expect(outcome).toBeInstanceOf(Error);
  });

  it("followsTheHost_whenItsValueNextChangesAfterARefusedWrite", async () => {
    const { result, rerender } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("bad"));
    act(() => void result.current[2]("bad", () => call.promise));
    await act(async () => call.resolve(false));
    expect(result.current[0]).toBe("bad");

    // A configuration is loaded. The refused text must not stay in the box, to be sent over what was just loaded.
    rerender({ value: "from-config" });

    expect(result.current[0]).toBe("from-config");
  });

  it("answersWhatTheCallAnswered_andCallsItWithTheText", async () => {
    const { result } = setup("");
    const seen: string[] = [];

    let refused: void | boolean = undefined;
    await act(async () => {
      refused = await result.current[2]("x", (text) => {
        seen.push(text);
        return false;
      });
    });
    let taken: void | boolean = false;
    await act(async () => {
      taken = await result.current[2]("y", (text) => {
        seen.push(text);
      });
    });

    expect(seen).toEqual(["x", "y"]);
    expect(refused).toBe(false);
    expect(taken).toBeUndefined();
  });
});

describe("useDraft with two commits in quick succession", () => {
  type Step = "A1" | "E1" | "A2" | "E2";

  const permutations = (steps: Step[]): Step[][] =>
    steps.length <= 1 ? [steps] : steps.flatMap((step, i) => permutations([...steps.slice(0, i), ...steps.slice(i + 1)]).map((rest) => [step, ...rest]));
  /** Every order in which the answers (A) and the host's echoes (E) of two sends can come, each pair in the order it was sent. */
  const orderings = permutations(["A1", "E1", "A2", "E2"]).filter(
    (order) => order.indexOf("A1") < order.indexOf("A2") && order.indexOf("E1") < order.indexOf("E2"),
  );

  it("hasSixOrderings", () => {
    expect(orderings).toHaveLength(6);
  });

  it.each(orderings.map((order) => [order.join(" ")]))("neverShowsAnOlderSendsTextOrAValueOtherThanTheNewest_%s", async (name) => {
    const order = name.split(" ") as Step[];
    const { result, rerender } = setup("V0");
    const call1 = pendingCall<boolean>();
    const call2 = pendingCall<boolean>();
    act(() => result.current[1]("T1"));
    act(() => void result.current[2]("T1", () => call1.promise));
    act(() => result.current[1]("T2"));
    act(() => void result.current[2]("T2", () => call2.promise));
    expect(result.current[0]).toBe("T2");

    // The first answer or echo to come belongs to the first send, and none of them changes what the box shows while the
    // second send is outstanding; the box is reconciled with the host when the last send settles.
    let host = "V0";
    let answered = 0;
    const lastAnswerBeforeLastEcho = order.indexOf("A2") < order.indexOf("E2");
    for (const step of order) {
      if (step === "E1" || step === "E2") {
        host = step === "E1" ? "T1" : "T2";
        rerender({ value: host });
      } else {
        const call = step === "A1" ? call1 : call2;
        await act(async () => call.resolve(true));
        answered++;
      }
      // While an answer is still to come, the newest text is what the box shows. Once both have come it shows the host's value.
      expect(result.current[0]).toBe(answered < 2 ? "T2" : host);
      // When the host's reply is applied before the call settles, which is how the page's callers work, no step shows anything else.
      if (!lastAnswerBeforeLastEcho) expect(result.current[0]).toBe("T2");
    }

    expect(result.current[0]).toBe("T2");
  });

  it("doesNotShowTheFirstEchoOrLoseAKeystroke_whenTheFirstEchoComesAfterTheSecondCommit", async () => {
    const { result, rerender } = setup("");
    const call1 = pendingCall<boolean>();
    const call2 = pendingCall<boolean>();
    act(() => result.current[1]("T1"));
    act(() => void result.current[2]("T1", () => call1.promise));
    act(() => result.current[1]("T2"));
    act(() => void result.current[2]("T2", () => call2.promise));

    rerender({ value: "T1" });
    expect(result.current[0]).toBe("T2");

    // A key typed now lands on the newest text, not on the echo of the older one.
    act(() => result.current[1](result.current[0] + "x"));
    rerender({ value: "T2" });
    await act(async () => call1.resolve(true));
    await act(async () => call2.resolve(true));

    expect(result.current[0]).toBe("T2x");
  });

  it("usesTheOutcomeOfTheNewestSend_whenTheAnswersComeOutOfOrder", async () => {
    const { result, rerender } = setup("V0");
    const call1 = pendingCall<boolean>();
    const call2 = pendingCall<boolean>();
    act(() => result.current[1]("t1"));
    act(() => void result.current[2]("t1", () => call1.promise));
    act(() => result.current[1]("t2 "));
    act(() => void result.current[2]("t2 ", () => call2.promise));
    rerender({ value: "t2" });

    // The newest send is taken in first, and the older one is refused after it.
    await act(async () => call2.resolve(true));
    expect(result.current[0]).toBe("t2 ");
    await act(async () => call1.resolve(false));

    expect(result.current[0]).toBe("t2");
  });
});
