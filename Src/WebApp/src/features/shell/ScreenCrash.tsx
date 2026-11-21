import { strings } from "../../strings";
import type { Screen } from "./Sidebar";
import "../../components/components.css";
import "./Shell.css";

interface ScreenCrashProps {
  /** The screen that threw. */
  screen: Screen;
  /** Draws the screen again. */
  onRetry: () => void;
}

/** What the content area shows when one screen could not be drawn: the rest of the window (sidebar, run bar, shortcuts) still works. */
export default function ScreenCrash({ screen, onRetry }: ScreenCrashProps) {
  const text = strings.screenCrash;
  return (
    <div className="crash in-screen" role="alert">
      <h1>{text.title(strings.nav[screen])}</h1>
      <p>{text.body}</p>
      <button type="button" className="btn primary" onClick={onRetry}>
        {text.retry}
      </button>
    </div>
  );
}
