import { strings } from "../../strings";

interface EmptyStateProps {
  onPick: () => void;
  onLoadConfig: () => void;
}

const steps = [
  { title: "Add what you want to protect", text: "Files, a solution or a project." },
  { title: "Pick a protection level", text: "Standard is selected for you." },
  { title: "Run", text: "Press Obfuscate or Ctrl+Enter." },
];

/** First launch: what to do, in the order to do it. The window itself accepts drops; the buttons are the other way in. */
export default function EmptyState({ onPick, onLoadConfig }: EmptyStateProps) {
  return (
    <div className="files-wrap">
      <div>
        <h1 className="title">{strings.files.emptyTitle}</h1>
        <p className="sub">{strings.files.emptySub}</p>
      </div>

      <div className="drop" role="group" aria-label={strings.files.dropZone}>
        <svg className="ring" viewBox="0 0 24 24" aria-hidden="true">
          <g fill="none" stroke="var(--accent)" strokeWidth="4.5">
            <circle cx="12" cy="12" r="8" strokeDasharray="13.96 36.31" transform="rotate(130 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="8.38 41.89" opacity=".6" transform="rotate(230 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="8.38 41.89" opacity=".6" transform="rotate(70 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="6.98 43.29" opacity=".28" transform="rotate(290 12 12)" />
            <circle cx="12" cy="12" r="8" strokeDasharray="6.98 43.29" opacity=".28" transform="rotate(20 12 12)" />
          </g>
        </svg>
        <h2>{strings.files.dropTitle}</h2>
        <div className="row">
          <button type="button" className="btn primary lg" onClick={onPick}>
            {strings.files.addFiles}
          </button>
          <button type="button" className="btn lg" onClick={onPick}>
            {strings.files.openSolution}
          </button>
        </div>
        <p className="types">
          Accepts <span className="mono">.dll .exe .cs .sln .slnx .csproj .vbproj .fsproj</span>, and <span className="mono">.json</span> to load a
          configuration.
        </p>
      </div>

      <div>
        <div className="label steps-label">{strings.files.howItWorks}</div>
        <ol className="steps">
          {steps.map((step, i) => (
            <li className="card" key={step.title}>
              <span className="n" aria-hidden="true">{i + 1}</span>
              <div>
                <b>{step.title}</b>
                <span>{step.text}</span>
              </div>
            </li>
          ))}
        </ol>
      </div>

      <div>
        <button type="button" className="linkbtn" onClick={onLoadConfig}>
          {strings.files.loadConfig}
        </button>
      </div>
    </div>
  );
}
