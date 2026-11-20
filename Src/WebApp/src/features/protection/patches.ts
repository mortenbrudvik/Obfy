import type { SettingsPatch } from "../../types/domain";
import type { TechniqueContent } from "../../content/content";

/**
 * The patch that switches a technique on or off. Most techniques have an `enabled` option; the few without one say in
 * their content which options the switch turns on and which it turns off.
 */
export function switchPatch(id: string, content: TechniqueContent | undefined, on: boolean): SettingsPatch {
  const keys = content?.switch ? (on ? content.switch.on : content.switch.off) : null;
  if (!keys) return { [`${id}.enabled`]: on };
  return Object.fromEntries(keys.map((key) => [`${id}.${key}`, on]));
}
