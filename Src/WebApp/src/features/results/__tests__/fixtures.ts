import { emptyFiles, notRunResults, readyRun } from "../../../test/snapshots";
import type { FileRow, InputSnapshot, LogLine, ResultsSnapshot, RunSnapshot, SymbolNode, SymbolsPage } from "../../../types/domain";

export const finishedResults: ResultsSnapshot = {
  ...notRunResults,
  revision: 5,
  state: "Finished",
  groups: [
    { name: "Renamed", chips: [{ label: "Types", value: 128 }, { label: "Methods", value: 912 }] },
    { name: "Encrypted", chips: [{ label: "Strings", value: 340 }] },
    { name: "Total", chips: [{ label: "Total", value: 1380 }] },
  ],
  elapsedMs: 4200,
  symbolCount: 3,
  hasReport: true,
  previewPaths: ["C:/out/MyApp.dll", "C:/out/MyApp.Core.dll"],
};

export const runningResults: ResultsSnapshot = { ...notRunResults, revision: 3, state: "Running" };

export const runningRun: RunSnapshot = { ...readyRun, status: "Running", canStart: false, indeterminate: true, total: 3, completed: 1 };

export const row = (over: Partial<FileRow> & { name: string }): FileRow => ({
  id: `id-${over.name}`, path: `C:/src/${over.name}`, origin: "Added", kind: "Assembly", size: 2048, sizeText: "2 KB",
  included: true, status: "Success", skipReason: null, outputPath: null, error: null, ...over,
});

export const filesOf = (...files: FileRow[]): InputSnapshot => ({ ...emptyFiles, revision: 2, files, hasIncludedFiles: true });

export const line = (id: number, level: LogLine["level"], message: string): LogLine => ({
  id, timestamp: `12:00:0${id}.000`, level, message,
});

export const node = (over: Partial<SymbolNode> & { id: string; originalName: string }): SymbolNode => ({
  obfuscatedName: null, kind: "Type", isContainer: false, childCount: 0, expanded: false, ...over,
});

export const page = (items: SymbolNode[], over: Partial<SymbolsPage> = {}): SymbolsPage => ({
  revision: 5, parentId: null, total: items.length, offset: 0, items, totalSymbols: 3, hasNoSymbols: false, hasNoSearchMatches: false, ...over,
});
