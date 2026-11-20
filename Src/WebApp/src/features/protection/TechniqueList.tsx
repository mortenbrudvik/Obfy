import Badge from "../../components/Badge";
import Toggle from "../../components/Toggle";
import { content, SCOPE_ID } from "../../content/content";
import { strings } from "../../strings";
import type { SettingsSnapshot, TechniqueState } from "../../types/domain";
import { ruleCount } from "./scope";
import { contentFor } from "./techniqueContent";

interface Row {
  id: string;
  group: string;
  name: string;
  summary: string;
  windowsOnly: boolean;
  /** Absent for the scope row, which has no switch. */
  technique?: TechniqueState;
  rules?: number;
}

/** Every row there is, by group in the content's order: the host's techniques, then the scope row. Unknown techniques come last under "Other". */
function allRows(settings: SettingsSnapshot): Row[] {
  const known = Object.keys(content.techniques);
  const byId = new Map(settings.techniques.map((technique) => [technique.id, technique]));
  const ordered = [...known.filter((id) => byId.has(id)), ...settings.techniques.map((t) => t.id).filter((id) => !known.includes(id))];

  const rows: Row[] = ordered.map((id) => {
    const words = contentFor(id);
    return { id, group: words.group, name: words.name, summary: words.summary, windowsOnly: words.windowsOnly === true, technique: byId.get(id) };
  });
  rows.push({ id: SCOPE_ID, group: content.scope.group, name: content.scope.name, summary: content.scope.summary, windowsOnly: false, rules: ruleCount(settings) });

  const groups = [...content.groups, strings.protection.otherGroup];
  return groups.flatMap((group) => rows.filter((row) => row.group === group));
}

/** The rows that match what was typed, against name, summary and group. */
function matching(rows: Row[], query: string): Row[] {
  const q = query.trim().toLowerCase();
  if (!q) return rows;
  return rows.filter((row) => `${row.name} ${row.summary} ${row.group}`.toLowerCase().includes(q));
}

interface TechniqueListProps {
  settings: SettingsSnapshot;
  selected: string;
  query: string;
  onQuery: (query: string) => void;
  onSelect: (id: string) => void;
  onSwitch: (id: string, on: boolean) => void;
}

/** The left list: a search box and the techniques under their group labels, each with its switch, summary and badges. */
export default function TechniqueList({ settings, selected, query, onQuery, onSelect, onSwitch }: TechniqueListProps) {
  const rows = matching(allRows(settings), query);
  const groups = [...new Set(rows.map((row) => row.group))];

  return (
    <section className="mlist" aria-label={strings.protection.techniques}>
      <div className="find">
        <label className="vh" htmlFor="protection-find">
          {strings.protection.findOption}
        </label>
        <input
          id="protection-find"
          type="search"
          className="field"
          placeholder={strings.protection.findOption}
          autoComplete="off"
          value={query}
          onChange={(e) => onQuery(e.target.value)}
        />
      </div>

      <div className="mscroll">
        {rows.length === 0 && <p className="empty-find">{strings.protection.noMatch(query.trim())}</p>}
        {groups.map((group) => (
          <div key={group}>
            <div className="label">{group}</div>
            <ul>
              {rows
                .filter((row) => row.group === group)
                .map((row) => (
                  <TechniqueRow
                    key={row.id}
                    row={row}
                    selected={selected === row.id}
                    onSelect={() => onSelect(row.id)}
                    onSwitch={(on) => onSwitch(row.id, on)}
                  />
                ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  );
}

interface TechniqueRowProps {
  row: Row;
  selected: boolean;
  onSelect: () => void;
  onSwitch: (on: boolean) => void;
}

function TechniqueRow({ row, selected, onSelect, onSwitch }: TechniqueRowProps) {
  const technique = row.technique;
  const unavailable = technique !== undefined && !technique.available;
  const classes = ["trow", selected ? "sel" : "", unavailable ? "na" : ""].filter(Boolean).join(" ");

  return (
    <li className={classes}>
      {technique ? (
        <Toggle label={row.name} checked={technique.enabled && technique.available} disabled={unavailable} onChange={onSwitch} />
      ) : (
        <span className="swgap" />
      )}
      <button type="button" className="tmain" aria-current={selected ? "true" : undefined} onClick={onSelect}>
        <span className="n">
          {row.name}
          {row.windowsOnly && !unavailable && <Badge>{strings.protection.windowsOnly}</Badge>}
          {technique?.differsFromLevel && !unavailable && <Badge tone="accent">{strings.protection.changed}</Badge>}
          {row.rules !== undefined && <Badge>{row.rules === 0 ? strings.protection.noRules : strings.protection.rules(row.rules)}</Badge>}
        </span>
        <span className="s">{unavailable ? technique.unavailableReason : row.summary}</span>
      </button>
    </li>
  );
}
