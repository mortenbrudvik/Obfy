export interface WebView {
  addEventListener(type: string, listener: (event: MessageEvent<unknown>) => void): void;
  /**
   * Posts a message to the host together with objects the host can read natively. `File` objects from a drop arrive as
   * CoreWebView2File, which carries the file's real path; a page cannot learn a path any other way.
   */
  postMessageWithAdditionalObjects(message: unknown, additionalObjects: ArrayLike<unknown>): void;
  hostObjects: {
    apibridge: {
      InvokeMethod(service: string, method: string, jsonParams: string): Promise<string>;
    };
  };
}
