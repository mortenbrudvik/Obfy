import type { LogLine } from "../../types/domain";

/** The line the host writes between two runs (LogBuffer.RunSeparator). */
export const RUN_SEPARATOR = "———";

/** How the log is shown: kept by the page, so it survives a change of tab. */
export interface LogOptions {
  problemsOnly: boolean;
  timestamps: boolean;
  wrap: boolean;
}

export const defaultLogOptions: LogOptions = { problemsOnly: false, timestamps: true, wrap: false };

export const isSeparator = (line: LogLine) => line.message === RUN_SEPARATOR;

export const isProblem = (line: LogLine) => line.level === "Warning" || line.level === "Error";

/**
 * The lines to show. All of them, or in problems-only just the warnings and errors with the separators between runs
 * that still have a problem on each side: a separator that would open the list, close it or follow another is dropped.
 */
export function visibleLines(lines: readonly LogLine[], problemsOnly: boolean): LogLine[] {
  if (!problemsOnly) return [...lines];

  const shown: LogLine[] = [];
  for (const line of lines) {
    if (isSeparator(line)) {
      const last = shown[shown.length - 1];
      if (last && !isSeparator(last)) shown.push(line);
    } else if (isProblem(line)) shown.push(line);
  }
  if (shown.length > 0 && isSeparator(shown[shown.length - 1])) shown.pop();
  return shown;
}

/** The lines as plain text for the clipboard: "[time ]Level message", one per line; a separator stands alone. */
export function logText(lines: readonly LogLine[], timestamps: boolean): string {
  return lines
    .map((line) => (isSeparator(line) ? RUN_SEPARATOR : `${timestamps ? `${line.timestamp} ` : ""}${line.level} ${line.message}`))
    .join("\n");
}
