import { Component, type ErrorInfo, type ReactNode } from "react";
import { strings } from "../strings";
import "./components.css";

interface ErrorBoundaryProps {
  children: ReactNode;
  /** What the Reload button does; the page reloads by default. The host keeps the files and settings, so nothing is lost. */
  onReload?: () => void;
}

interface ErrorBoundaryState {
  failed: boolean;
}

/**
 * Catches a throw while the page draws (a snapshot it did not expect) and says so, with a way to reload, instead of
 * leaving React to unmount everything and the window blank.
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { failed: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(error, info.componentStack);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    const text = strings.crash;
    return (
      <div className="crash" role="alert">
        <h1>{text.title}</h1>
        <p>{text.body}</p>
        <button type="button" className="btn primary" onClick={this.props.onReload ?? (() => window.location.reload())}>
          {text.reload}
        </button>
      </div>
    );
  }
}
