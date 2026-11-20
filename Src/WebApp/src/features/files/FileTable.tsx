import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";
import { strings } from "../../strings";
import type { FileRow } from "../../types/domain";

interface FileTableProps {
  files: FileRow[];
  /** Editing is locked (a run is in progress). */
  locked: boolean;
  onSetIncluded: (id: string, included: boolean) => void;
  onRemove: (ids: string[]) => void;
  onReveal: (id: string) => void;
}

/** A project row has no output of its own, so the host never includes it; the box must not offer to. */
function canInclude(file: FileRow): boolean {
  return file.kind !== "Project";
}

/** The status column: what the run did with the row, or why it is not part of one. */
function statusText(file: FileRow): string {
  const s = strings.files.status;
  switch (file.status) {
    case "Processing":
      return s.processing;
    case "Success":
      return s.done;
    case "Error":
      return file.error ?? s.failed;
    case "Skipped":
      return file.skipReason ?? strings.files.notIncluded;
    default:
      return file.included ? s.ready : (file.skipReason ?? strings.files.notIncluded);
  }
}

function statusClass(file: FileRow): string {
  if (file.status === "Error") return "st error";
  if (file.status === "Skipped" || !file.included) return "st skipped";
  return "st";
}

interface RowMenuProps {
  file: FileRow;
  locked: boolean;
  onReveal: () => void;
  onRemove: () => void;
}

/** The row's "⋯" menu: show it in its folder, or take it off the list. */
function RowMenu({ file, locked, onReveal, onRemove }: RowMenuProps) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onMouseDown = (event: globalThis.MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onMouseDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onMouseDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const choose = (action: () => void) => () => {
    setOpen(false);
    action();
  };

  return (
    <div className="rowmenu" ref={root}>
      <button
        type="button"
        className="more"
        aria-label={`${strings.files.moreActions} ${file.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        {"⋯"}
      </button>
      {open && (
        <div className="menu" role="menu">
          <button type="button" role="menuitem" onClick={choose(onReveal)}>
            {strings.files.showInFolder}
          </button>
          <button type="button" role="menuitem" disabled={locked} onClick={choose(onRemove)}>
            {strings.files.remove}
          </button>
        </div>
      )}
    </div>
  );
}

/** The file list: include box, name, origin, kind, size, status and the row menu. Click selects; Delete removes the selection. */
export default function FileTable({ files, locked, onSetIncluded, onRemove, onReveal }: FileTableProps) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  // A row that has left the list is no longer selected.
  const selectedIds = files.filter((f) => selected.has(f.id)).map((f) => f.id);

  const onControl = (event: { target: EventTarget }) => (event.target as HTMLElement).closest("input, button") !== null;

  const select = (id: string) => (event: MouseEvent) => {
    if (onControl(event)) return;
    setSelected((current) => {
      if (event.ctrlKey || event.metaKey) {
        const next = new Set(current);
        if (!next.delete(id)) next.add(id);
        return next;
      }
      return new Set([id]);
    });
  };

  const onKeyDown = (id: string) => (event: KeyboardEvent) => {
    if (event.key !== "Delete" || locked || onControl(event)) return;
    event.preventDefault();
    onRemove(selectedIds.includes(id) ? selectedIds : [id]);
  };

  return (
    <div className="card">
      <table className="ftable">
        <thead>
          <tr>
            <th><span className="vh">{strings.files.include}</span></th>
            <th>{strings.files.columns.name}</th>
            <th>{strings.files.columns.origin}</th>
            <th>{strings.files.columns.kind}</th>
            <th className="num">{strings.files.columns.size}</th>
            <th>{strings.files.columns.status}</th>
            <th><span className="vh">{strings.files.actions}</span></th>
          </tr>
        </thead>
        <tbody>
          {files.map((file) => {
            const isSelected = selectedIds.includes(file.id);
            const className = [file.included ? "" : "skip", isSelected ? "selected" : ""].filter(Boolean).join(" ") || undefined;
            return (
              <tr
                key={file.id}
                className={className}
                aria-selected={isSelected}
                tabIndex={0}
                title={file.path}
                onClick={select(file.id)}
                onKeyDown={onKeyDown(file.id)}
              >
                <td>
                  <input
                    type="checkbox"
                    className="chk"
                    aria-label={`${strings.files.include} ${file.name}`}
                    title={canInclude(file) ? undefined : strings.files.projectCannotBeIncluded}
                    checked={file.included}
                    disabled={locked || !canInclude(file)}
                    onChange={(e) => onSetIncluded(file.id, e.target.checked)}
                  />
                </td>
                <td className="name mono">{file.name}</td>
                <td>{file.origin}</td>
                <td>{file.kind}</td>
                <td className="num">{file.sizeText}</td>
                <td><span className={statusClass(file)}>{statusText(file)}</span></td>
                <td>
                  <RowMenu file={file} locked={locked} onReveal={() => onReveal(file.id)} onRemove={() => onRemove([file.id])} />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
