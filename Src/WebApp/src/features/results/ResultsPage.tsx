import { useState } from "react";
import Notice from "../../components/Notice";
import Tabs from "../../components/Tabs";
import { tabPanelProps } from "../../components/tabIds";
import { strings } from "../../strings";
import { useInput } from "../files/useInput";
import LogTab from "./LogTab";
import { defaultLogOptions, type LogOptions } from "./logFormat";
import PreviewTab from "./PreviewTab";
import RunningView from "./RunningView";
import SummaryTab from "./SummaryTab";
import SymbolsTab from "./SymbolsTab";
import { useLog } from "./useLog";
import { useResults } from "./useResults";
import "../../components/components.css";
import "./ResultsPage.css";

type TabKey = "summary" | "symbols" | "preview" | "log";

const tabs = (Object.keys(strings.results.tab) as TabKey[]).map((id) => ({ id, label: strings.results.tab[id] }));
const TABS_PREFIX = "results";

/** The Results screen: nothing before a run, progress during one, and the tabs once it has finished. */
export default function ResultsPage() {
  const text = strings.results;
  const { results, failed, symbolsGeneration, error: exportError, exportedTo, querySymbols, getPreview, exportMap, exportReport } = useResults();
  const { input, error: inputError, reveal, openOutput } = useInput();
  const { lines, clear } = useLog();
  const [tab, setTab] = useState<TabKey>("summary");
  const [logOptions, setLogOptions] = useState<LogOptions>(defaultLogOptions);

  if (!results) {
    return (
      <div className="results-page">
        {failed && <Notice tone="error">{text.unreachable}</Notice>}
      </div>
    );
  }

  const files = input?.files ?? [];
  const problem = exportError ?? inputError;

  if (results.state === "NotRun") {
    return (
      <div className="nothing">
        <svg viewBox="0 0 24 24" width="36" height="36" aria-hidden="true">
          <g fill="none" stroke="var(--accent)" strokeWidth="4.5">
            <circle cx="12" cy="12" r="8" strokeDasharray="13.96 36.31" transform="rotate(130 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="8.38 41.89" opacity=".6" transform="rotate(230 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="8.38 41.89" opacity=".6" transform="rotate(70 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="6.98 43.29" opacity=".28" transform="rotate(290 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="6.98 43.29" opacity=".28" transform="rotate(20 12 12)" />
          </g>
        </svg>
        <h2>{text.nothingTitle}</h2>
        <p className="sub">{text.nothingText}</p>
      </div>
    );
  }

  if (results.state === "Running") {
    return (
      <div className="results-page">
        <RunningView files={files} log={lines} />
      </div>
    );
  }

  const runFiles = files.filter((file) => file.included && (file.outputPath || file.status === "Error"));
  const written = runFiles.filter((file) => file.outputPath).length;
  const failedCount = runFiles.length - written;
  const summary = [
    text.fileCount(written),
    ...(failedCount > 0 ? [text.failedCount(failedCount)] : []),
    text.seconds(results.elapsedMs),
  ].join(" · ");

  return (
    <div className="results-page">
      <div className="rhead">
        <div>
          <h1 className="title" data-testid="results-finished">{text.finished}</h1>
          <p className="sub">{summary}</p>
        </div>
        <div className="acts">
          <button type="button" className="btn" onClick={() => void openOutput()}>
            {text.openOutput}
          </button>
          <button type="button" className="btn" disabled={!results.hasReport} onClick={() => void exportReport()}>
            {text.exportReport}
          </button>
          <button type="button" className="btn" disabled={results.symbolCount === 0} onClick={() => void exportMap()}>
            {text.exportMap}
          </button>
        </div>
      </div>

      {problem && <Notice tone="error">{problem}</Notice>}
      {exportedTo && <p className="help saved" role="status">{text.savedTo(exportedTo)}</p>}

      <Tabs label={text.tabs} tabs={tabs} selected={tab} onChange={(id) => setTab(id as TabKey)} idPrefix={TABS_PREFIX} />
      <div {...tabPanelProps(TABS_PREFIX, tab)}>
        {tab === "summary" && <SummaryTab groups={results.groups} files={files} onReveal={(id) => void reveal(id)} />}
        {tab === "symbols" && <SymbolsTab querySymbols={querySymbols} generation={symbolsGeneration} />}
        {tab === "preview" && <PreviewTab paths={results.previewPaths} selectedPath={results.selectedPreviewPath} getPreview={getPreview} />}
        {tab === "log" && <LogTab lines={lines} options={logOptions} onOptionsChange={setLogOptions} onClear={() => void clear()} />}
      </div>
    </div>
  );
}
