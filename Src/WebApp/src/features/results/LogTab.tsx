import { useEffect, useRef, useState } from "react";
import Toggle from "../../components/Toggle";
import { strings } from "../../strings";
import type { LogLine } from "../../types/domain";
import LogLines from "./LogLines";
import { logText, visibleLines, type LogOptions } from "./logFormat";

interface LogTabProps {
  lines: readonly LogLine[];
  options: LogOptions;
  onOptionsChange: (options: LogOptions) => void;
  onClear: () => void;
}

const COPIED_MS = 2000;

/** The whole log: all lines or problems only, with timestamps and wrapping on request, to copy or clear. */
export default function LogTab({ lines, options, onOptionsChange, onClear }: LogTabProps) {
  const text = strings.results.logView;
  const shown = visibleLines(lines, options.problemsOnly);
  const [copied, setCopied] = useState(false);
  const [copyFailed, setCopyFailed] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(logText(shown, options.timestamps));
      setCopyFailed(false);
      setCopied(true);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), COPIED_MS);
    } catch {
      setCopied(false);
      setCopyFailed(true);
    }
  };

  return (
    <>
      <div className="logbar">
        <div className="seg" role="group" aria-label={text.filter}>
          <button type="button" aria-pressed={!options.problemsOnly} onClick={() => onOptionsChange({ ...options, problemsOnly: false })}>
            {text.all}
          </button>
          <button type="button" aria-pressed={options.problemsOnly} onClick={() => onOptionsChange({ ...options, problemsOnly: true })}>
            {text.problems}
          </button>
        </div>
        <label>
          <Toggle label={text.timestamps} checked={options.timestamps} onChange={(timestamps) => onOptionsChange({ ...options, timestamps })} />
          {text.timestamps}
        </label>
        <label>
          <Toggle label={text.wrap} checked={options.wrap} onChange={(wrap) => onOptionsChange({ ...options, wrap })} />
          {text.wrap}
        </label>
        <button type="button" className="btn sm" onClick={() => void copy()}>
          {copied ? text.copied : text.copy}
        </button>
        <button type="button" className="btn sm" disabled={lines.length === 0} onClick={onClear}>
          {text.clear}
        </button>
        {copyFailed && <span className="help" role="alert">{text.copyFailed}</span>}
      </div>
      <div className={`log ${options.wrap ? "wrap" : "nowrap"} tall`} role="log" tabIndex={0} aria-label={strings.results.log}>
        {shown.length === 0 ? (
          <p className="help">{lines.length === 0 ? text.empty : text.noProblems}</p>
        ) : (
          <LogLines lines={shown} timestamps={options.timestamps} />
        )}
      </div>
    </>
  );
}
