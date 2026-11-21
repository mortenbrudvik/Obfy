import { useState } from "react";
import { useAppAction } from "../../hooks/useAppAction";
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

  const [pick, pickError] = useAppAction(async () => setAnswered(await api.files.pick()), strings.files.failed.pick);
  const [clear, clearError] = useAppAction(async () => setAnswered(await api.files.clear()), strings.files.failed.clear);
  const [removeRows, removeError] = useAppAction(async (ids: string[]) => {
    for (const id of ids) setAnswered(await api.files.remove(id));
  }, strings.files.failed.remove);
  const [setIncluded, includeError] = useAppAction(
    async (id: string, included: boolean) => setAnswered(await api.files.setIncluded(id, included)),
    strings.files.failed.include,
  );
  const [reveal, revealError] = useAppAction(async (id: string) => setAnswered(await api.files.reveal(id)), strings.files.failed.reveal);
  const [openOutput, openOutputError] = useAppAction(async () => setAnswered(await api.files.openOutput()), strings.files.failed.reveal);
  const [openConfig, configError] = useAppAction(async () => {
    await api.settings.openConfig();
  }, strings.files.failed.config);
  const [setOutput, outputError] = useAppAction(async (folder: string) => setAnswered(await api.output.set(folder)), strings.files.failed.output);
  const [pickFolder, pickFolderError] = useAppAction(async () => setAnswered(await api.output.pickFolder()), strings.files.failed.output);
  const [pickSymbolMap, pickSymbolMapError] = useAppAction(async () => setAnswered(await api.output.pickSymbolMap()), strings.files.failed.output);
  const [setSymbolMap, symbolMapError] = useAppAction(
    async (change: { enabled?: boolean; path?: string }) => setAnswered(await api.output.setSymbolMap(change)),
    strings.files.failed.output,
  );

  const error =
    pickError ?? clearError ?? removeError ?? includeError ?? revealError ?? openOutputError ?? configError ??
    outputError ?? pickFolderError ?? pickSymbolMapError ?? symbolMapError;

  return {
    input,
    failed,
    error,
    pick: async () => void (await pick()),
    clear: async () => void (await clear()),
    remove: async (ids) => void (await removeRows(ids)),
    setIncluded: async (id, included) => void (await setIncluded(id, included)),
    reveal: async (id) => void (await reveal(id)),
    openOutput: async () => void (await openOutput()),
    openConfig: async () => void (await openConfig()),
    setOutput,
    pickFolder: async () => void (await pickFolder()),
    pickSymbolMap: async () => void (await pickSymbolMap()),
    setSymbolMap,
  };
}
