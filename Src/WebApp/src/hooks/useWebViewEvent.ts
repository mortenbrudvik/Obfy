import { useEffect, useRef } from "react";
import { apiClient } from "../interop/WebViewApiClient";

export function useWebViewEvent<T>(eventName: string, callback: (data: T) => void) {
  const callbackRef = useRef(callback);
  useEffect(() => { callbackRef.current = callback; });

  useEffect(() => {
    const unsubscribe = apiClient.on<T>(eventName, (data) => callbackRef.current(data));
    return unsubscribe;
  }, [eventName]);
}
