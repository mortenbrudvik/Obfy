import { useState } from "react";
import ErrorBoundary from "./components/ErrorBoundary";
import { FIRST_GUIDE_TOPIC } from "./content/guideContent";
import FilesPage from "./features/files/FilesPage";
import GuidePage from "./features/guide/GuidePage";
import ProtectionPage from "./features/protection/ProtectionPage";
import ResultsPage from "./features/results/ResultsPage";
import Shell from "./features/shell/Shell";
import type { Screen } from "./features/shell/Sidebar";

/** The shell hosting the current screen. The Guide opens at its first topic from the sidebar and F1, and at a given topic from a "Read more" link. */
export default function App() {
  const [screen, setScreen] = useState<Screen>("files");
  const [guideTopic, setGuideTopic] = useState(FIRST_GUIDE_TOPIC);

  const navigate = (next: Screen) => {
    // Opening the Guide from elsewhere starts at its first topic; asking for it while it is open leaves the reader where they are.
    if (next === "guide" && screen !== "guide") setGuideTopic(FIRST_GUIDE_TOPIC);
    setScreen(next);
  };
  const openGuide = (topic: string) => {
    setGuideTopic(topic);
    setScreen("guide");
  };

  return (
    <ErrorBoundary>
      <Shell screen={screen} onNavigate={navigate}>
        {screen === "files" ? (
          <FilesPage />
        ) : screen === "protection" ? (
          <ProtectionPage onOpenGuide={openGuide} />
        ) : screen === "results" ? (
          <ResultsPage />
        ) : (
          <GuidePage topic={guideTopic} onTopicChange={setGuideTopic} />
        )}
      </Shell>
    </ErrorBoundary>
  );
}
