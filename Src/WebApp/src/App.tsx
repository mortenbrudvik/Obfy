import { useState } from "react";
import Shell from "./features/shell/Shell";
import type { Screen } from "./features/shell/Sidebar";

const placeholders: Record<Screen, { title: string; text: string }> = {
  files: { title: "Files", text: "The files to protect and where the output goes." },
  protection: { title: "Protection", text: "The protection level and each technique." },
  results: { title: "Results", text: "What the last run did." },
  guide: { title: "Guide", text: "How Obfy works, and the keyboard shortcuts." },
};

/** The shell hosting the current screen. The screens are placeholders until their tasks fill them in. */
export default function App() {
  const [screen, setScreen] = useState<Screen>("files");
  const placeholder = placeholders[screen];

  return (
    <Shell screen={screen} onNavigate={setScreen}>
      <div className="placeholder-screen">
        <h1 className="title">{placeholder.title}</h1>
        <p className="sub">{placeholder.text}</p>
      </div>
    </Shell>
  );
}
