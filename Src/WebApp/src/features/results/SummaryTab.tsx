import { strings } from "../../strings";
import type { FileRow, StatisticGroup } from "../../types/domain";

interface SummaryTabProps {
  groups: readonly StatisticGroup[];
  /** The rows of the file list that were run; a row with an output is listed with it, a failed one with why. */
  files: readonly FileRow[];
  onReveal: (id: string) => void;
}

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

/**
 * What the run did, in tiles by group, and where each output went. The host leaves out every counter that is zero and
 * every group with none, so the tiles are drawn as sent: nothing here decides what to hide.
 */
export default function SummaryTab({ groups, files, onReveal }: SummaryTabProps) {
  const text = strings.results;
  const outputs = files.filter((file) => file.included && (file.outputPath || file.status === "Error"));

  return (
    <>
      {groups.length === 0 ? (
        <p className="help">{text.noTransformations}</p>
      ) : (
        <div className="groups">
          {groups.map((group) => (
            // A group is as wide as it has tiles, so a tile is the same size in every group.
            <div key={group.name} className="tiles-group" style={{ flex: `${group.chips.length} 1 ${group.chips.length * 110}px` }}>
              <div className="label">{group.name}</div>
              <div className="tiles" style={{ gridTemplateColumns: `repeat(${group.chips.length}, minmax(0, 1fr))` }}>
                {group.chips.map((chip) => (
                  <div key={chip.label} className="card tile">
                    <b>{chip.value.toLocaleString("en-US")}</b>
                    <span>{chip.label}</span>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="label rfiles-label" id="output-files">{text.outputFiles}</div>
      {outputs.length === 0 ? (
        <p className="help">{text.noOutputs}</p>
      ) : (
        <div className="card">
          <ul className="flist" aria-labelledby="output-files">
            {outputs.map((file) => {
              const failed = file.status === "Error";
              return (
                <li key={file.id}>
                  <span className={failed ? "ico fail" : "ico done"} role="img" aria-label={failed ? text.fileState.failed : text.fileState.done}>
                    {failed ? CROSS : CHECK}
                  </span>
                  <div className="grow">
                    <span className="mono">{file.name}</span>
                    <span className={failed ? "path" : "path mono"} title={failed ? undefined : (file.outputPath ?? undefined)}>
                      {failed ? (file.error ?? text.fileState.failed) : file.outputPath}
                    </span>
                  </div>
                  {!failed && (
                    <button type="button" className="linkbtn" aria-label={text.revealFile(file.name)} onClick={() => onReveal(file.id)}>
                      {text.reveal}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </>
  );
}
