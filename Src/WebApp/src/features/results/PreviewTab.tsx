import { useEffect, useState } from "react";
import Notice from "../../components/Notice";
import Select from "../../components/Select";
import { failure } from "../../interop/errorMessage";
import { strings } from "../../strings";
import type { PreviewResult } from "../../types/domain";

interface PreviewTabProps {
  /** The output assemblies the host will decompile; it refuses any other path. */
  paths: readonly string[];
  /** The assembly the host already decompiled, if any. */
  selectedPath: string | null;
  getPreview: (path: string) => Promise<PreviewResult>;
}

type Loaded = { path: string; text: string; error: string | null };

const baseName = (path: string) => path.split(/[\\/]/).pop() || path;

/** The decompiled C# of one output assembly. Nothing is decompiled until one is chosen. */
export default function PreviewTab({ paths, selectedPath, getPreview }: PreviewTabProps) {
  const text = strings.results.preview;
  const [chosen, setChosen] = useState<string>(selectedPath && paths.includes(selectedPath) ? selectedPath : "");
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!chosen) return;
    let cancelled = false;
    getPreview(chosen).then(
      (result) => {
        if (!cancelled) setLoaded({ path: chosen, text: result.text, error: result.error });
      },
      (err) => {
        if (!cancelled) setLoaded({ path: chosen, text: "", error: failure(text.failed, err) });
      },
    );
    return () => {
      cancelled = true;
    };
  }, [chosen, getPreview, text.failed]);

  // What is shown is what was decompiled for the assembly in the box; a stale answer is never shown for another one.
  const current = loaded && loaded.path === chosen ? loaded : null;

  if (paths.length === 0) return <p className="help">{text.noAssemblies}</p>;

  return (
    <>
      <div className="row previewbar">
        <label htmlFor="preview-assembly" className="previewlabel">{text.assembly}</label>
        <Select
          id="preview-assembly"
          value={chosen}
          onChange={setChosen}
          options={[
            ...(chosen ? [] : [{ value: "", label: text.choose }]),
            ...paths.map((path) => ({ value: path, label: baseName(path) })),
          ]}
        />
        <span className="help">{text.help}</span>
      </div>
      {current?.error && <Notice tone="error">{current.error}</Notice>}
      {current && !current.error && (
        <pre className="code" role="region" tabIndex={0} aria-label={`${text.assembly}: ${baseName(current.path)}`}>
          {current.text || text.empty}
        </pre>
      )}
    </>
  );
}
