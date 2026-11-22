import Notice from "../../components/Notice";
import { usePushedState } from "../../hooks/usePushedState";
import { events } from "../../interop/api";
import { strings } from "../../strings";
import type { NoticeLevel, RunSnapshot } from "../../types/domain";
import EmptyState from "./EmptyState";
import FileTable from "./FileTable";
import OutputCard from "./OutputCard";
import { useInput } from "./useInput";
import "../../components/components.css";
import "./FilesPage.css";

const noticeTone: Record<NoticeLevel, "info" | "warn" | "error"> = { Info: "info", Warning: "warn", Error: "error" };

/** The Files screen: first-launch guidance when the list is empty, otherwise the list and where the output goes. */
export default function FilesPage() {
  const { input, failed, error, pick, clear, remove, setIncluded, reveal, openConfig, setOutput, pickFolder, pickSymbolMap, setSymbolMap } = useInput();
  const { state: run } = usePushedState<RunSnapshot>("run", "get", events.runChanged);
  // The run captured its inputs when it started; the list is not edited under it.
  const locked = run?.status === "Running";

  if (!input) {
    return (
      <div className="files-page">
        <h1 className="title">{strings.files.title}</h1>
        {failed && <Notice tone="error">{strings.files.unreachable}</Notice>}
      </div>
    );
  }

  const problem = error && <Notice tone="error">{error}</Notice>;
  const notices = input.notices.map((notice, i) => (
    <Notice key={i} tone={noticeTone[notice.level]} title={notice.title}>
      {notice.message}
    </Notice>
  ));

  if (input.files.length === 0) {
    return (
      <>
        {problem}
        {notices}
        <EmptyState onPick={() => void pick()} onLoadConfig={() => void openConfig()} />
      </>
    );
  }

  return (
    <div className="files-page">
      <div>
        <h1 className="title">{strings.files.title}</h1>
        {input.files.some((file) => file.origin === "Solution") && <p className="sub">{strings.files.fromSolution}</p>}
      </div>

      {problem}
      {notices}

      <div className="toolbar">
        <button type="button" className="btn" disabled={locked} onClick={() => void pick()}>
          {strings.files.addFiles}
        </button>
        <button type="button" className="btn" disabled={locked} onClick={() => void clear()}>
          {strings.files.clear}
        </button>
        {locked && <span className="help">{strings.files.editingLocked}</span>}
      </div>

      <FileTable
        files={input.files}
        locked={locked}
        onSetIncluded={(id, included) => void setIncluded(id, included)}
        onRemove={(ids) => void remove(ids)}
        onReveal={(id) => void reveal(id)}
      />

      {run?.modeExplanation && <Notice>{run.modeExplanation}</Notice>}

      <OutputCard
        input={input}
        locked={locked}
        onSetOutput={setOutput}
        onPickFolder={() => void pickFolder()}
        onPickSymbolMap={() => void pickSymbolMap()}
        onSetSymbolMap={setSymbolMap}
      />
    </div>
  );
}
