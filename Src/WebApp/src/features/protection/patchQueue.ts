import type { SettingsPatch } from "../../types/domain";

interface Queued {
  patch: SettingsPatch;
  /** One per caller whose change this call carries. */
  waiters: ((took: boolean) => void)[];
}

const sameKeys = (a: SettingsPatch, b: SettingsPatch) => {
  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length && keys.every((key) => key in b);
};

/**
 * Sends changes to the host one at a time, in the order they were made. The host takes every call on a thread of its
 * own, so two calls in flight at once can be applied in either order and leave it on the older value while the page
 * shows the newer one. While a call is out, a change to the same options as the one queued last (the next keystroke in
 * a number box, the next tick of a slider) replaces it instead of queueing behind it: the latest value wins, and every
 * caller still hears whether the host took it in. Changes to different options are never merged, so a value the host
 * refuses cannot take an unrelated change down with it.
 *
 * `send` answers whether the host took the patch in; one that throws counts as a refusal.
 */
export function createPatchQueue(send: (patch: SettingsPatch) => Promise<boolean>): (patch: SettingsPatch) => Promise<boolean> {
  const queue: Queued[] = [];
  let draining = false;

  const drain = async () => {
    if (draining) return;
    draining = true;
    try {
      for (let next = queue.shift(); next; next = queue.shift()) {
        let took = false;
        try {
          took = await send(next.patch);
        } catch {
          took = false;
        }
        next.waiters.forEach((resolve) => resolve(took));
      }
    } finally {
      draining = false;
    }
  };

  return (patch) =>
    new Promise<boolean>((resolve) => {
      const last = queue[queue.length - 1];
      if (last && sameKeys(last.patch, patch)) {
        last.patch = patch;
        last.waiters.push(resolve);
      } else {
        queue.push({ patch, waiters: [resolve] });
      }
      void drain();
    });
}
