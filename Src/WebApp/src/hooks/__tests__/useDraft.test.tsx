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

  it("keepsWhatIsBeingTyped_whenTheHostChangesWithoutACommit", () => {
    const { result, rerender } = setup("a");
    act(() => result.current[1]("typed"));

    rerender({ value: "b" });

    expect(result.current[0]).toBe("typed");
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

  it("keepsTextTypedAfterASend_whenTheEchoComesAfterTheAnswer", async () => {
    const { result, rerender } = setup("");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("C:\\out"));
    act(() => void result.current[2]("C:\\out", () => call.promise));
    act(() => result.current[1]("C:\\out2"));

    await act(async () => call.resolve(true));
    rerender({ value: "C:\\out" });

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

  it("treatsARefusedTextAsAnEditTheHostHasNotSeen_soALaterHostChangeDoesNotDropIt", async () => {
    const { result, rerender } = setup("old");
    const call = pendingCall<boolean>();
    act(() => result.current[1]("bad"));
    act(() => void result.current[2]("bad", () => call.promise));
    await act(async () => call.resolve(false));

    rerender({ value: "from-config" });

    expect(result.current[0]).toBe("bad");
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
