import { api } from "../interop/api";
import { strings } from "../strings";
import { useAppAction } from "./useAppAction";

/**
 * Opens the online guide in the default browser. `open` takes a section id the host knows (Obfy.App GuideLinks) and
 * never rejects; with no id, or one the host does not know, the guide opens at its start. When the browser cannot be
 * started, `error` says so until the next call.
 */
export function useOnlineGuide(): [open: (topic?: string) => Promise<boolean>, error: string | null] {
  return useAppAction(async (topic?: string) => {
    await api.app.openGuide(topic);
  }, strings.guide.onlineFailed);
}
