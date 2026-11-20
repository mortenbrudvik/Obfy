import { useState } from "react";
import FilesPage from "./features/files/FilesPage";
import ProtectionPage from "./features/protection/ProtectionPage";
import ResultsPage from "./features/results/ResultsPage";
import Shell from "./features/shell/Shell";
import type { Screen } from "./features/shell/Sidebar";

function GuidePlaceholder() {
  return (
    <div className="placeholder-screen">
      <h1 className="title">Guide</h1>
      <p className="sub">How Obfy works, and the keyboard shortcuts.</p>
    </div>
  );
}

/** The shell hosting the current screen. Files, Protection and Results are built; the Guide is a placeholder until its task fills it in. */
export default function App() {
  const [screen, setScreen] = useState<Screen>("files");

  return (
    <Shell screen={screen} onNavigate={setScreen}>
      {screen === "files" ? (
        <FilesPage />
      ) : screen === "protection" ? (
        // The Guide opens at its first topic until it takes a topic of its own.
        <ProtectionPage onOpenGuide={() => setScreen("guide")} />
      ) : screen === "results" ? (
        <ResultsPage />
      ) : (
        <GuidePlaceholder />
      )}
    </Shell>
  );
}
