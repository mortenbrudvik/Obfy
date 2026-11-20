import Badge from "../../components/Badge";
import Meter from "../../components/Meter";
import { strings } from "../../strings";

interface LevelInfo {
  id: string;
  name: string;
  description: string;
  /** Protection, runtime cost and compatibility risk, 0 to 3 steps each; Custom has none. */
  meters: [number, number, number] | null;
  recommended?: boolean;
}

// Display metadata for the four levels. What a level switches on is the host's: these are only the words and the
// meters, which let the cards be compared at a glance.
const levels: LevelInfo[] = [
  { id: "Minimal", name: "Minimal", description: "Renames symbols and strips debug information. The safest choice.", meters: [1, 0, 1] },
  { id: "Standard", name: "Standard", description: "Minimal, plus encrypted string literals and attribute clean-up.", meters: [2, 1, 1], recommended: true },
  {
    id: "Aggressive",
    name: "Aggressive",
    description:
      "Everything in Standard, plus control flow, constant, resource and method body encryption, reference proxies, debugger, integrity and memory checks, and decompiler decoys. Test your app thoroughly.",
    meters: [3, 2, 3],
  },
  { id: "Custom", name: "Custom", description: "Your own mix. Changing any option below moves you here.", meters: null },
];

const meterLabels = [strings.protection.meters.protection, strings.protection.meters.cost, strings.protection.meters.risk];

interface LevelCardsProps {
  /** The host's current level. */
  level: string;
  onSelect: (level: string) => void;
}

/** The four level cards, one a radio each. */
export default function LevelCards({ level, onSelect }: LevelCardsProps) {
  return (
    <div className="levels" role="radiogroup" aria-labelledby="protection-level-label">
      {levels.map((info) => (
        <button
          key={info.id}
          type="button"
          role="radio"
          className="lvl"
          aria-checked={level === info.id}
          onClick={() => onSelect(info.id)}
        >
          <span className="t">
            {info.name}
            {info.recommended && <Badge tone="accent">{strings.protection.levelRecommended}</Badge>}
          </span>
          <span className="d">{info.description}</span>
          {info.meters && (
            <div className="meters">
              {info.meters.map((value, i) => (
                <Meter key={meterLabels[i]} label={meterLabels[i]} value={value} />
              ))}
            </div>
          )}
        </button>
      ))}
    </div>
  );
}
