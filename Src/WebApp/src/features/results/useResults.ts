import { useCallback, useState } from "react";
import { useAppAction } from "../../hooks/useAppAction";
import { usePushedState } from "../../hooks/usePushedState";
import { api, events } from "../../interop/api";
import { strings } from "../../strings";
import type { PreviewResult, ResultsSnapshot, ResultsState, SymbolsPage } from "../../types/domain";

export interface UseResults {
  /** The host's view of the results; null until the first read answers. */
  results: ResultsSnapshot | null;
  /** The first read failed twice and nothing has been pushed. */
  failed: boolean;
  /**
   * Counts how many times the symbols were reloaded: it rises when the run starts over or finishes (the state
   * changes) or the map changes size, and not when the revision rises for another reason (the report arriving, a
   * preview being chosen). A symbol tree built for one generation is good for as long as the generation holds, so the
   * page keys it on this and never on `revision`.
   */
  symbolsGeneration: number;
  /** Why the last export failed, until the next one. */
  error: string | null;
  /** Where the last export was saved, until the next one; null after a cancelled or failed one. */
  exportedTo: string | null;
  /** One window of the symbol tree, fetched when asked for (not part of the pushed snapshot). */
  querySymbols: (text: string | null, parentId: string | null, offset: number, limit: number) => Promise<SymbolsPage>;
  /** Decompiles one output assembly. Rejects when the host refuses the path. */
  getPreview: (path: string) => Promise<PreviewResult>;
  exportMap: () => Promise<void>;
  exportReport: () => Promise<void>;
}

interface Generation {
  count: number;
  state: ResultsState | null;
  symbolCount: number;
}

/**
 * The results: the summary is read once and kept current by `results.changed`. The symbol tree and the decompiled text
 * are not in it; they are asked for on demand, and the symbol pages carry the global results revision, which rises
 * with every unrelated change, so they are tied to `symbolsGeneration` instead.
 */
export function useResults(): UseResults {
  const { state: results, failed } = usePushedState<ResultsSnapshot>("results", "summary", events.resultsChanged);

  // Derived while rendering (React's way to adjust state to a prop that changed): the generation moves with the state
  // and the size of the map, which only change when the results are dropped or loaded.
  const [generation, setGeneration] = useState<Generation>({ count: 0, state: null, symbolCount: -1 });
  if (results && (results.state !== generation.state || results.symbolCount !== generation.symbolCount)) {
    setGeneration({ count: generation.count + 1, state: results.state, symbolCount: results.symbolCount });
  }

  const [exportedTo, setExportedTo] = useState<string | null>(null);
  const [exportMap, mapError] = useAppAction(async () => {
    setExportedTo(null);
    const done = await api.results.exportMap();
    if (done.exported) setExportedTo(done.path);
  }, strings.results.exportFailed);
  const [exportReport, reportError] = useAppAction(async () => {
    setExportedTo(null);
    const done = await api.results.exportReport();
    if (done.exported) setExportedTo(done.path);
  }, strings.results.exportFailed);

  const querySymbols = useCallback(
    (text: string | null, parentId: string | null, offset: number, limit: number) => api.results.symbols(text, parentId, offset, limit),
    [],
  );
  const getPreview = useCallback((path: string) => api.preview.get(path), []);

  return {
    results,
    failed,
    symbolsGeneration: generation.count,
    error: mapError ?? reportError,
    exportedTo,
    querySymbols,
    getPreview,
    exportMap: async () => void (await exportMap()),
    exportReport: async () => void (await exportReport()),
  };
}
