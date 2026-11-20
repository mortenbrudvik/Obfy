import { strings } from "../../strings";
import type { LogLine } from "../../types/domain";
import { isSeparator } from "./logFormat";

interface LogLinesProps {
  lines: readonly LogLine[];
  timestamps: boolean;
}

/** The log's lines, each coloured by its level; the line between two runs is drawn as a rule. */
export default function LogLines({ lines, timestamps }: LogLinesProps) {
  return (
    <>
      {lines.map((line) =>
        isSeparator(line) ? (
          <div key={line.id} className="sepline" role="separator" aria-label={strings.results.logView.newRun}>
            {line.message}
          </div>
        ) : (
          <div key={line.id} className="ll">
            {timestamps && <span className="ts">{line.timestamp}</span>}
            <span className={`lv lv-${line.level.toLowerCase()}`}>{line.level}</span>
            <span className="ms">{line.message}</span>
          </div>
        ),
      )}
    </>
  );
}
