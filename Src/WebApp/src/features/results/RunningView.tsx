import { useEffect, useRef } from "react";
import { strings } from "../../strings";
import type { FileRow, LogLine } from "../../types/domain";
import LogLines from "./LogLines";

/** The newest lines kept in the live tail; the whole log is on the Log tab once the run is done. */
const TAIL_LINES = 200;

type FileState = "done" | "running" | "waiting" | "failed";

function stateOf(file: FileRow): FileState {
  return file.status === "Success" ? "done" : file.status === "Processing" ? "running" : file.status === "Error" ? "failed" : "waiting";
}

interface RunningViewProps {
  files: readonly FileRow[];
  log: readonly LogLine[];
}

const iconClass: Record<FileState, string> = { done: "done", running: "run", waiting: "", failed: "fail" };

const CHECK = (
  <svg viewBox="0 0 10 10" aria-hidden="true">
    <path d="M2 5.2l2.1 2.1L8 3" />
  </svg>
);
const CROSS = (
  <svg viewBox="0 0 10 10" aria-hidden="true">
    <path d="M2.5 2.5l5 5M7.5 2.5l-5 5" />
  </svg>
);

/** A run in progress: each included file with where it stands, and the log as it is written. */
export default function RunningView({ files, log }: RunningViewProps) {
  const text = strings.results;
  const included = files.filter((file) => file.included);
  const tail = log.slice(-TAIL_LINES);

  // The newest line stays in view, as it would in a terminal.
  const box = useRef<HTMLDivElement>(null);
  const lastLineId = tail[tail.length - 1]?.id;
  useEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [lastLineId]);

  return (
    <>
      <div className="rhead">
        <div>
          <h1 className="title">{text.running}</h1>
          <p className="sub">{text.runningSub}</p>
        </div>
      </div>

      <div className="sec">
        <span className="label" id="running-files">{text.files}</span>
      </div>
      <div className="card">
        <ul className="flist" aria-labelledby="running-files">
          {included.map((file) => {
            const state = stateOf(file);
            return (
              <li key={file.id}>
                <span className={`ico ${iconClass[state]}`} role="img" aria-label={text.fileState[state]}>
                  {state === "done" ? CHECK : state === "failed" ? CROSS : null}
                </span>
                <div className="grow">
                  <span className="mono">{file.name}</span>
                  <span className="path">{state === "failed" ? (file.error ?? text.fileState.failed) : text.fileText[state]}</span>
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      <div className="sec">
        <span className="label">{text.log}</span>
      </div>
      <div className="log wrap short" ref={box} role="log" aria-label={text.log} aria-live="off" tabIndex={0}>
        <LogLines lines={tail} timestamps />
      </div>
    </>
  );
}
