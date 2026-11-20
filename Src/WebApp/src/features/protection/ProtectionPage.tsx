import { useState } from "react";
import Notice from "../../components/Notice";
import Select from "../../components/Select";
import { content, SCOPE_ID } from "../../content/content";
import { strings } from "../../strings";
import type { SettingsPatch } from "../../types/domain";
import LevelCards from "./LevelCards";
import { switchPatch } from "./patches";
import ScopeDetail from "./ScopeDetail";
import TechniqueDetail from "./TechniqueDetail";
import TechniqueList from "./TechniqueList";
import { contentFor } from "./techniqueContent";
import { useProtection } from "./useProtection";
import "../../components/components.css";
import "./ProtectionPage.css";

interface ProtectionPageProps {
  /** Opens the Guide at a topic; the Guide itself is another screen. */
  onOpenGuide?: (topic: string) => void;
}

const baseName = (path: string) => path.split(/[\\/]/).pop() || path;

/** The Protection screen: the level, the target runtime, and every technique with its own explanation and options. */
export default function ProtectionPage({ onOpenGuide }: ProtectionPageProps) {
  const { settings, failed, error, applyLevel, update, setRuntimeProfile, openConfig, saveConfig, pickSigningKey } = useProtection();
  const [selected, setSelected] = useState("symbolRenaming");
  const [query, setQuery] = useState("");

  if (!settings) {
    return (
      <div className="protection-page">
        <h1 className="title">{strings.protection.title}</h1>
        {failed && <Notice tone="error">{strings.protection.unreachable}</Notice>}
      </div>
    );
  }

  const send = (patch: SettingsPatch) => update(patch);
  const guide = (topic: string) => onOpenGuide?.(topic);

  const technique = settings.techniques.find((t) => t.id === selected) ?? (selected === SCOPE_ID ? undefined : settings.techniques[0]);

  const profiles = settings.choices.runtimeProfiles.length > 0 ? settings.choices.runtimeProfiles : Object.keys(content.runtimeProfiles);
  const chip = configChip(settings.configPath, settings.dirty);

  return (
    <div className="protection-page">
      <div className="phead">
        <h1 className="title">{strings.protection.title}</h1>
        <div className="cfg">
          <span className={settings.dirty || !settings.configPath ? "chip" : "chip saved"} title={settings.configPath ?? undefined}>
            <b>{chip.name}</b> · {chip.state}
          </span>
          <button type="button" className="btn sm" onClick={() => void openConfig()}>
            {strings.protection.openConfig}
          </button>
          <button type="button" className="btn sm" onClick={() => void saveConfig()}>
            {strings.protection.saveConfig}
          </button>
        </div>
      </div>

      {error && <Notice tone="error">{error}</Notice>}

      <div className="sec">
        <span className="label" id="protection-level-label">
          {strings.protection.level}
        </span>
      </div>
      <LevelCards level={settings.level} onSelect={(level) => void applyLevel(level)} />

      <div className="rtrow">
        <label htmlFor="protection-runtime" className="label">
          {strings.protection.targetRuntime}
        </label>
        <Select
          id="protection-runtime"
          aria-describedby="protection-runtime-help"
          value={settings.runtimeProfile}
          options={profiles.map((profile) => ({ value: profile, label: content.runtimeProfiles[profile] ?? profile }))}
          onChange={(profile) => void setRuntimeProfile(profile)}
        />
        <span className="help" id="protection-runtime-help">
          {strings.protection.targetRuntimeHelp}
        </span>
      </div>

      <div className="sec">
        <span className="label">{strings.protection.techniques}</span>
      </div>
      <div className="split">
        <TechniqueList
          settings={settings}
          selected={technique?.id ?? SCOPE_ID}
          query={query}
          onQuery={setQuery}
          onSelect={setSelected}
          onSwitch={(id, on) => send(switchPatch(id, contentFor(id), on))}
        />
        {technique ? (
          <TechniqueDetail
            key={technique.id}
            technique={technique}
            snapshot={settings}
            onUpdate={send}
            onPickKey={() => void pickSigningKey()}
            onOpenGuide={guide}
          />
        ) : (
          <ScopeDetail snapshot={settings} onUpdate={send} onOpenGuide={guide} />
        )}
      </div>
    </div>
  );
}

/** What the chip beside Open and Save says: which file the settings belong to, and whether it has the latest. */
function configChip(configPath: string | null, dirty: boolean): { name: string; state: string } {
  const name = configPath ? baseName(configPath) : strings.protection.configFileFallback;
  if (dirty) return { name, state: strings.protection.configUnsavedChanges };
  return { name, state: configPath ? strings.protection.configSaved : strings.protection.configNotSaved };
}
