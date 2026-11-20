export interface WebView {
  addEventListener(type: string, listener: (event: MessageEvent<unknown>) => void): void;
  hostObjects: {
    apibridge: {
      InvokeMethod(service: string, method: string, jsonParams: string): Promise<string>;
    };
  };
}
