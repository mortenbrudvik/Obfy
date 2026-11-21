import { Component, useEffect, useRef, type ErrorInfo, type ReactNode } from "react";
import { api } from "../interop/api";
import { strings } from "../strings";
import "./components.css";

interface ErrorBoundaryProps {
  children: ReactNode;
  /**
   * What to show once a child has thrown; `retry` draws the children again. A screen's boundary passes its own in-place
   * view. Without one this is the page's last resort: the full-page crash view, which also reveals the window.
   */
  fallback?: (retry: () => void) => ReactNode;
  /** What the crash view's Reload button does; the page reloads by default. The host keeps the files and settings, so nothing is lost. */
  onReload?: () => void;
}

interface ErrorBoundaryState {
  failed: boolean;
}

/**
 * Catches a throw while the page draws (a snapshot it did not expect) and says so, instead of leaving React to unmount
 * everything and the window blank.
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { failed: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(error, info.componentStack);
  }

  private retry = () => this.setState({ failed: false });

  render() {
    if (!this.state.failed) return this.props.children;
    if (this.props.fallback) return this.props.fallback(this.retry);
    return <CrashView onReload={this.props.onReload ?? (() => window.location.reload())} />;
  }
}

/**
 * The page's last resort. The host keeps its window hidden until the page says it has drawn (`app.ready`) or its own
 * timeout passes, and a throw before the shell mounted means the shell never will say so: this view does, once, so the
 * window opens on the message rather than after the wait. A refused call is ignored; the host reveals the window anyway.
 */
function CrashView({ onReload }: { onReload: () => void }) {
  const sent = useRef(false);
  useEffect(() => {
    if (sent.current) return;
    sent.current = true;
    api.app.ready().catch(() => {});
  }, []);

  const text = strings.crash;
  return (
    <div className="crash" role="alert">
      <h1>{text.title}</h1>
      <p>{text.body}</p>
      <button type="button" className="btn primary" onClick={onReload}>
        {text.reload}
      </button>
    </div>
  );
}
