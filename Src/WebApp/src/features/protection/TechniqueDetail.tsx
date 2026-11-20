import Notice from "../../components/Notice";
import { strings } from "../../strings";
import type { SettingsPatch, SettingsSnapshot, TechniqueState } from "../../types/domain";
import { DetailHead, DetailSection, GuideLink } from "./DetailParts";
import OptionControl from "./OptionControl";
import { switchPatch } from "./patches";
import { contentFor } from "./techniqueContent";

interface TechniqueDetailProps {
  technique: TechniqueState;
  snapshot: SettingsSnapshot;
  onUpdate: (patch: SettingsPatch) => void;
  onPickKey: () => void;
  onOpenGuide: (topic: string) => void;
}

/**
 * The right-hand panel for one technique: what it does, what to know before using it, every option with its help
 * always shown, and a before and after where there is one. Everything that varies comes from the host's snapshot.
 */
export default function TechniqueDetail({ technique, snapshot, onUpdate, onPickKey, onOpenGuide }: TechniqueDetailProps) {
  const words = contentFor(technique.id);
  const on = technique.enabled && technique.available;
  const errorFor = (key: string) => snapshot.errors.find((e) => e.field === `${technique.id}.${key}`)?.message ?? null;

  return (
    <section className="detail" aria-label={words.name}>
      <DetailHead
        name={words.name}
        summary={words.summary}
        switchState={{ on, disabled: !technique.available, onChange: (next) => onUpdate(switchPatch(technique.id, words, next)) }}
      />

      {!technique.available && (
        <div className="dsec">
          <p className="unavail">{strings.protection.unavailable(technique.unavailableReason ?? "")}</p>
        </div>
      )}

      {words.what && (
        <DetailSection title={strings.protection.whatItDoes}>
          <p>{words.what}</p>
        </DetailSection>
      )}

      {(words.good.length > 0 || words.securityNote) && (
        <DetailSection title={strings.protection.goodToKnow}>
          {words.good.length > 0 && (
            <ul>
              {words.good.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          )}
          {words.securityNote && (
            <div className="secnote">
              <Notice tone="warn" title={words.securityNote.title}>
                {words.securityNote.text}
              </Notice>
            </div>
          )}
        </DetailSection>
      )}

      {words.options.length > 0 && (
        <DetailSection title={strings.protection.options}>
          <div className="opts">
            {words.options.map((option) => (
              <OptionControl
                key={option.key}
                technique={technique.id}
                option={option}
                values={technique.options}
                choices={snapshot.choices}
                error={errorFor(option.key)}
                onUpdate={onUpdate}
                onPickKey={onPickKey}
              />
            ))}
          </div>
        </DetailSection>
      )}

      {words.beforeAfter && (
        <DetailSection title={strings.protection.beforeAndAfter}>
          <div className="ba">
            <div>
              <span>{strings.protection.before}</span>
              <code>{words.beforeAfter.before}</code>
            </div>
            <div className="after">
              <span>{strings.protection.after}</span>
              <code>{words.beforeAfter.after}</code>
            </div>
          </div>
        </DetailSection>
      )}

      <GuideLink onClick={() => onOpenGuide(words.guideTopic)} />
    </section>
  );
}
