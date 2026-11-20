import type { ReactNode } from "react";
import { strings } from "../../strings";
import Keycap from "../../components/Keycap";

export type Screen = "files" | "protection" | "results" | "guide";

/** Where the results stand, for the dot beside Results. */
export type ResultsDot = "none" | "running" | "done";

interface SidebarProps {
  screen: Screen;
  onNavigate: (screen: Screen) => void;
  /** Included files; the badge beside Files is hidden while the list is empty. */
  includedCount: number;
  fileCount: number;
  /** The protection level's name, once known. */
  level: string | null;
  results: ResultsDot;
  version: string | null;
}

const resultsLabel: Record<ResultsDot, string> = {
  none: strings.nav.resultsNone,
  running: strings.nav.resultsRunning,
  done: strings.nav.resultsFinished,
};

interface NavItemProps {
  screen: Screen;
  current: Screen;
  label: string;
  onNavigate: (screen: Screen) => void;
  children?: ReactNode;
}

function NavItem({ screen, current, label, onNavigate, children }: NavItemProps) {
  return (
    <button type="button" className="nav" aria-current={screen === current ? "page" : undefined} onClick={() => onNavigate(screen)}>
      <span>{label}</span>
      {children}
    </button>
  );
}

/** The 208px section list: Files, Protection and Results, with the Guide and the version at the bottom. */
export default function Sidebar({ screen, onNavigate, includedCount, fileCount, level, results, version }: SidebarProps) {
  const nav = { current: screen, onNavigate };
  return (
    <aside className="sidebar" aria-label={strings.shell.sections}>
      <NavItem {...nav} screen="files" label={strings.nav.files}>
        {fileCount > 0 && (
          <span className="pill" aria-label={`${includedCount} ${includedCount === 1 ? "file" : "files"} included`}>
            {includedCount}
          </span>
        )}
      </NavItem>
      <NavItem {...nav} screen="protection" label={strings.nav.protection}>
        {level && <span className="meta">{level}</span>}
      </NavItem>
      <NavItem {...nav} screen="results" label={strings.nav.results}>
        <span className={results === "none" ? "dot" : `dot ${results}`} role="img" aria-label={resultsLabel[results]} />
      </NavItem>
      <div className="side-foot">
        <NavItem {...nav} screen="guide" label={strings.nav.guide}>
          <Keycap>F1</Keycap>
        </NavItem>
        {version && <div className="version">Obfy {version}</div>}
      </div>
    </aside>
  );
}
