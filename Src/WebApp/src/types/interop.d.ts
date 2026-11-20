import type { WebView } from "./WebView";

declare global {
  interface Window {
    chrome?: {
      webview?: WebView;
    };
  }
}
