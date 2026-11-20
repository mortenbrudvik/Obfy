import type { ReactNode } from "react";
import Toggle from "../../components/Toggle";
import { strings } from "../../strings";

interface DetailHeadProps {
  name: string;
  summary: string;
  /** The switch, when the technique has one. */
  switchState?: { on: boolean; disabled: boolean; onChange: (on: boolean) => void };
  /** What stands in place of the switch (the number of rules). */
  badge?: ReactNode;
}

/** The title of a detail panel with its On/Off switch. */
export function DetailHead({ name, summary, switchState, badge }: DetailHeadProps) {
  return (
    <>
      <div className="dhead">
        <h2>{name}</h2>
        {switchState ? (
          <label>
            {switchState.on ? strings.protection.on : strings.protection.off}
            <Toggle label={strings.protection.enable(name)} checked={switchState.on} disabled={switchState.disabled} onChange={switchState.onChange} />
          </label>
        ) : (
          badge
        )}
      </div>
      <p className="dsum">{summary}</p>
    </>
  );
}

/** A titled section of a detail panel. */
export function DetailSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="dsec">
      <div className="label">{title}</div>
      {children}
    </div>
  );
}

/** The "Read more in the Guide" link at the foot of a detail panel. */
export function GuideLink({ onClick }: { onClick: () => void }) {
  return (
    <p className="guide-link">
      <button type="button" className="linkbtn" onClick={onClick}>
        {strings.protection.readMore}
      </button>
    </p>
  );
}
