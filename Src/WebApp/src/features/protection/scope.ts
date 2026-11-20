import type { SettingsSnapshot } from "../../types/domain";

/** How many exclusion and inclusion patterns are set, for the badge on the scope row. */
export function ruleCount(settings: SettingsSnapshot): number {
  return [settings.exclusions, settings.inclusions].reduce(
    (total, lists) => total + lists.namespaces.length + lists.types.length + lists.methods.length,
    0,
  );
}
