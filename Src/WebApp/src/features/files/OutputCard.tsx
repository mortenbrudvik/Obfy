import type { KeyboardEvent } from "react";
import Field from "../../components/Field";
import Toggle from "../../components/Toggle";
import { usePendingEdit, type FlushResult } from "../../hooks/pendingEdits";
import { useDraft } from "../../hooks/useDraft";
import { strings } from "../../strings";
import type { InputSnapshot } from "../../types/domain";

interface OutputCardProps {
  input: InputSnapshot;
  /** Editing is locked (a run is in progress). */
  locked: boolean;
  /** Answers whether the host took the folder in; `false` is a refusal. */
  onSetOutput: (folder: string) => Promise<boolean> | void;
  onPickFolder: () => void;
  onPickSymbolMap: () => void;
  /** Answers whether the host took the change in; `false` is a refusal. */
  onSetSymbolMap: (change: { enabled?: boolean; path?: string }) => Promise<boolean> | void;
}

/** Where the output goes, and whether to write the symbol map. */
export default function OutputCard({ input, locked, onSetOutput, onPickFolder, onPickSymbolMap, onSetSymbolMap }: OutputCardProps) {
  const [folder, setFolder] = useDraft(input.outputFolder);
  const [mapPath, setMapPath] = useDraft(input.symbolMapPath);

  // A box is committed when it is left or on Enter. A shortcut that acts first (run, save) flushes it through here,
  // so the run never starts with the folder that was there before the typing. While a run goes the boxes are locked
  // and there is nothing to send, and a symbol-map path left in a box that is no longer shown is not sent.
  const commitFolder = async (): Promise<FlushResult> => {
    if (locked || folder === input.outputFolder) return "idle";
    return (await onSetOutput(folder)) === false ? "failed" : "sent";
  };
  const commitMapPath = async (): Promise<FlushResult> => {
    if (locked || !input.writeSymbolMap || mapPath === input.symbolMapPath) return "idle";
    return (await onSetSymbolMap({ path: mapPath })) === false ? "failed" : "sent";
  };
  usePendingEdit(commitFolder);
  usePendingEdit(commitMapPath);
  const onEnter = (commit: () => Promise<FlushResult>) => (event: KeyboardEvent) => {
    if (event.key === "Enter") void commit();
  };

  return (
    <section className="card outcard" aria-labelledby="files-output-heading">
      <h2 id="files-output-heading">{strings.files.output}</h2>

      <Field label={strings.files.outputFolder} help={strings.files.outputHelp}>
        {(control) => (
          <div className="rowfield">
            <input
              {...control}
              type="text"
              className="field"
              value={folder}
              placeholder={input.outputHint}
              disabled={locked}
              onChange={(e) => setFolder(e.target.value)}
              onBlur={() => void commitFolder()}
              onKeyDown={onEnter(commitFolder)}
            />
            <button type="button" className="btn" disabled={locked} onClick={onPickFolder}>
              {strings.files.browse}
            </button>
          </div>
        )}
      </Field>

      {input.outputWarning && <p className="help warn-text">{input.outputWarning}</p>}

      <div className="toggle-row">
        <Toggle
          id="files-map-toggle"
          label={strings.files.symbolMap}
          aria-describedby="files-map-help"
          checked={input.writeSymbolMap}
          disabled={locked}
          onChange={(enabled) => onSetSymbolMap({ enabled })}
        />
        <div className="fl tight">
          <label htmlFor="files-map-toggle">{strings.files.symbolMap}</label>
          <p className="help" id="files-map-help">{strings.files.symbolMapHelp}</p>
        </div>
      </div>

      {input.writeSymbolMap && (
        <Field label={strings.files.symbolMapFile} help={input.resolvedSymbolMapPath ? `Written to ${input.resolvedSymbolMapPath}` : undefined}>
          {(control) => (
            <div className="rowfield">
              <input
                {...control}
                type="text"
                className="field mono"
                value={mapPath}
                disabled={locked}
                onChange={(e) => setMapPath(e.target.value)}
                onBlur={() => void commitMapPath()}
                onKeyDown={onEnter(commitMapPath)}
              />
              <button type="button" className="btn" disabled={locked} aria-label={strings.files.browseSymbolMap} onClick={onPickSymbolMap}>
                {strings.files.browse}
              </button>
            </div>
          )}
        </Field>
      )}
    </section>
  );
}
