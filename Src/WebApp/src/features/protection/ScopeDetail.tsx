import Badge from "../../components/Badge";
import { content } from "../../content/content";
import type { Committed } from "../../hooks/useDraft";
import { strings } from "../../strings";
import type { SettingsPatch, SettingsSnapshot } from "../../types/domain";
import { DetailHead, DetailSection, GuideLink } from "./DetailParts";
import { ListOption } from "./OptionControl";
import { ruleCount } from "./scope";

interface ScopeDetailProps {
  snapshot: SettingsSnapshot;
  onUpdate: (patch: SettingsPatch) => Committed;
  onOpenGuide: (topic: string) => void;
}

/** The detail panel of the exclusion and inclusion rules, which the host keeps beside the techniques, not among them. */
export default function ScopeDetail({ snapshot, onUpdate, onOpenGuide }: ScopeDetailProps) {
  const words = content.scope;
  const count = ruleCount(snapshot);

  return (
    <section className="detail" aria-label={words.name}>
      <DetailHead name={words.name} summary={words.summary} badge={<Badge>{count === 0 ? strings.protection.noRules : strings.protection.rules(count)}</Badge>} />

      <DetailSection title={strings.protection.whatItDoes}>
        <p>{words.what}</p>
      </DetailSection>

      <DetailSection title={strings.protection.goodToKnow}>
        <ul>
          {words.good.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </DetailSection>

      <DetailSection title={strings.protection.rulesHeading}>
        <div className="opts">
          {words.lists.map((list) => (
            <ListOption
              key={`${list.set}.${list.key}`}
              option={{ key: list.key, type: "list", label: list.label, help: list.help, placeholder: list.placeholder }}
              value={snapshot[list.set][list.key]}
              error={null}
              onCommit={(value) => onUpdate({ [`${list.set}.${list.key}`]: value })}
            />
          ))}
        </div>
      </DetailSection>

      <GuideLink onClick={() => onOpenGuide(words.guideTopic)} />
    </section>
  );
}
