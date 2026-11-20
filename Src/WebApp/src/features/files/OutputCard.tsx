import type { KeyboardEvent } from "react";
import Field from "../../components/Field";
import Toggle from "../../components/Toggle";
import { useDraft } from "../../hooks/useDraft";
import { strings } from "../../strings";
import type { InputSnapshot } from "../../types/domain";

interface OutputCardProps {
  input: InputSnapshot;
  /** Editing is locked (a run is in progress). */
  locked: boolean;
  onSetOutput: (folder: string) => void;
  onPickFolder: () => void;
  onSetSymbolMap: (change: { enabled?: boolean; path?: string }) => void;
}

/** Where the output goes, and whether to write the symbol map. */
export default function OutputCard({ input, locked, onSetOutput, onPickFolder, onSetSymbolMap }: OutputCardProps) {
  const [folder, setFolder] = useDraft(input.outputFolder);
  const [mapPath, setMapPath] = useDraft(input.symbolMapPath);

  const commitFolder = () => {
    if (folder !== input.outputFolder) onSetOutput(folder);
  };
  const commitMapPath = () => {
    if (mapPath !== input.symbolMapPath) onSetSymbolMap({ path: mapPath });
  };
  const onEnter = (commit: () => void) => (event: KeyboardEvent) => {
    if (event.key === "Enter") commit();
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
              onBlur={commitFolder}
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
                onBlur={commitMapPath}
                onKeyDown={onEnter(commitMapPath)}
              />
            </div>
          )}
        </Field>
      )}
    </section>
  );
}
