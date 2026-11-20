import { useState } from "react";
import FilesPage from "./features/files/FilesPage";
import ProtectionPage from "./features/protection/ProtectionPage";
import Shell from "./features/shell/Shell";
import type { Screen } from "./features/shell/Sidebar";

const placeholders: Record<Exclude<Screen, "files" | "protection">, { title: string; text: string }> = {
  results: { title: "Results", text: "What the last run did." },
  guide: { title: "Guide", text: "How Obfy works, and the keyboard shortcuts." },
};

function Placeholder({ screen }: { screen: Exclude<Screen, "files" | "protection"> }) {
  const placeholder = placeholders[screen];
  return (
    <div className="placeholder-screen">
      <h1 className="title">{placeholder.title}</h1>
      <p className="sub">{placeholder.text}</p>
    </div>
  );
}

/** The shell hosting the current screen. Files and Protection are built; the other screens are placeholders until their tasks fill them in. */
export default function App() {
  const [screen, setScreen] = useState<Screen>("files");

  return (
    <Shell screen={screen} onNavigate={setScreen}>
      {screen === "files" ? (
        <FilesPage />
      ) : screen === "protection" ? (
        // The Guide opens at its first topic until it takes a topic of its own.
        <ProtectionPage onOpenGuide={() => setScreen("guide")} />
      ) : (
        <Placeholder screen={screen} />
      )}
    </Shell>
  );
}
