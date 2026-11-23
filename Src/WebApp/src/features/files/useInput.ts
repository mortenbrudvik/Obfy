import { useState } from "react";
import { useActionRunner } from "../../hooks/useAppAction";
import { usePushedState } from "../../hooks/usePushedState";
import { api, events } from "../../interop/api";
import { strings } from "../../strings";
import type { InputSnapshot } from "../../types/domain";

export interface UseInput {
  /** The host's view of the file list and output row; null until the first read answers. */
  input: InputSnapshot | null;
  /** The first read failed twice and nothing has been pushed. */
  failed: boolean;
  /** Why the last action failed, until the next one. */
  error: string | null;
  pick: () => Promise<void>;
  clear: () => Promise<void>;
  remove: (ids: string[]) => Promise<void>;
  setIncluded: (id: string, included: boolean) => Promise<void>;
  reveal: (id: string) => Promise<void>;
  openOutput: () => Promise<void>;
  openConfig: () => Promise<void>;
  /** Answers whether the host took the folder in. */
  setOutput: (folder: string) => Promise<boolean>;
  pickFolder: () => Promise<void>;
  pickSymbolMap: () => Promise<void>;
  /** Answers whether the host took the change in. */
  setSymbolMap: (change: { enabled?: boolean; path?: string }) => Promise<boolean>;
}

/**
 * The file list and the output row: read once, kept current by `files.changed`, and edited through the host. Each
 * edit answers the new snapshot; it is used when it is newer than the one held, so the page follows an edit even
 * before (or without) the push.
 */
export function useInput(): UseInput {
  const { state: pushed, failed } = usePushedState<InputSnapshot>("files", "get", events.filesChanged);
  const [answered, setAnswered] = useState<InputSnapshot | null>(null);
  const input = answered && (!pushed || answered.revision > pushed.revision) ? answered : pushed;

  const [act, error] = useActionRunner();

  return {
    input,
    failed,
    error,
    pick: async () => void (await act(strings.files.failed.pick, async () => setAnswered(await api.files.pick()))),
    clear: async () => void (await act(strings.files.failed.clear, async () => setAnswered(await api.files.clear()))),
    remove: async (ids) =>
      void (await act(strings.files.failed.remove, async () => {
        for (const id of ids) setAnswered(await api.files.remove(id));
      })),
    setIncluded: async (id, included) =>
      void (await act(strings.files.failed.include, async () => setAnswered(await api.files.setIncluded(id, included)))),
    reveal: async (id) => void (await act(strings.files.failed.reveal, async () => setAnswered(await api.files.reveal(id)))),
    openOutput: async () => void (await act(strings.files.failed.reveal, async () => setAnswered(await api.files.openOutput()))),
    openConfig: async () =>
      void (await act(strings.files.failed.config, async () => {
        await api.settings.openConfig();
      })),
    setOutput: (folder) => act(strings.files.failed.output, async () => setAnswered(await api.output.set(folder))),
    pickFolder: async () => void (await act(strings.files.failed.output, async () => setAnswered(await api.output.pickFolder()))),
    pickSymbolMap: async () => void (await act(strings.files.failed.output, async () => setAnswered(await api.output.pickSymbolMap()))),
    setSymbolMap: (change) => act(strings.files.failed.output, async () => setAnswered(await api.output.setSymbolMap(change))),
  };
}
