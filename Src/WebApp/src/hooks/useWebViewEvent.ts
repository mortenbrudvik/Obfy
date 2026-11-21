import { useEffect, useRef } from "react";
import { apiClient } from "../interop/WebViewApiClient";

/** Calls `callback` with the data of each `eventName` the host pushes while the component is mounted; null listens to nothing. */
export function useWebViewEvent<T>(eventName: string | null, callback: (data: T) => void) {
  const callbackRef = useRef(callback);
  useEffect(() => { callbackRef.current = callback; });

  useEffect(() => {
    if (eventName === null) return;
    const unsubscribe = apiClient.on<T>(eventName, (data) => callbackRef.current(data));
    return unsubscribe;
  }, [eventName]);
}
