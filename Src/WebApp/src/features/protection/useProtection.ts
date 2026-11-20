import { useCallback, useState } from "react";
import { usePushedState } from "../../hooks/usePushedState";
import { api, events } from "../../interop/api";
import { failure } from "../../interop/errorMessage";
import { strings } from "../../strings";
import type { SettingsPatch, SettingsSnapshot } from "../../types/domain";

export interface UseProtection {
  /** The host's view of the settings; null until the first read answers. */
  settings: SettingsSnapshot | null;
  /** The first read failed twice and nothing has been pushed. */
  failed: boolean;
  /** Why the last action failed, until the next one succeeds. */
  error: string | null;
  applyLevel: (level: string) => Promise<void>;
  update: (patch: SettingsPatch) => Promise<void>;
  setRuntimeProfile: (profile: string) => Promise<void>;
  openConfig: () => Promise<void>;
  saveConfig: () => Promise<void>;
  pickSigningKey: () => Promise<void>;
}

/**
 * The protection settings: read once, kept current by `settings.changed`, and changed through the host, which holds every
 * rule (the level moving to Custom, what a runtime rules out, what blocks a run). Each call answers the new snapshot;
 * it is used when it is newer than the one pushed, so the page follows an edit even before (or without) the push.
 */
export function useProtection(): UseProtection {
  const { state: pushed, failed } = usePushedState<SettingsSnapshot>("settings", "get", events.settingsChanged);
  const [answered, setAnswered] = useState<SettingsSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const settings = answered && (!pushed || answered.revision > pushed.revision) ? answered : pushed;

  // One error for the whole page: the next action that works clears it.
  const act = useCallback(
    (label: string, call: () => Promise<SettingsSnapshot>) => async () => {
      try {
        setAnswered(await call());
        setError(null);
      } catch (err) {
        setError(failure(label, err));
      }
    },
    [],
  );

  const update = useCallback(
    (patch: SettingsPatch) => act(strings.protection.failed.update, () => api.settings.update(patch))(),
    [act],
  );

  return {
    settings,
    failed,
    error,
    update,
    applyLevel: (level) => act(strings.protection.failed.level, () => api.settings.applyLevel(level))(),
    setRuntimeProfile: (profile) => update({ runtimeProfile: profile }),
    openConfig: act(strings.protection.failed.config, () => api.settings.openConfig()),
    saveConfig: act(strings.protection.failed.save, () => api.settings.saveConfig()),
    pickSigningKey: act(strings.protection.failed.key, () => api.settings.pickSigningKey()),
  };
}
