import { usePushedState } from "./hooks/usePushedState";
import type { AppInfo } from "./types/domain";

/** Smoke screen: proves the bridge by reading `app.info`, and the event channel by following `app.infoChanged`. */
export default function App() {
  const { state: info, failed } = usePushedState<AppInfo>("app", "info", "app.infoChanged");

  return (
    <main style={{ padding: 24 }}>
      <h1>Obfy</h1>
      <p role="status">
        {info ? `Connected. Version ${info.version}` : failed ? "Could not reach the app." : "Connecting…"}
      </p>
    </main>
  );
}
