import { useId, useState, type KeyboardEvent } from "react";
import { content, type OptionContent } from "../../content/content";
import Field from "../../components/Field";
import Select from "../../components/Select";
import Slider from "../../components/Slider";
import { useDraft } from "../../hooks/useDraft";
import { strings } from "../../strings";
import type { SettingsChoices, SettingsPatch } from "../../types/domain";

interface OptionControlProps {
  /** The technique the option belongs to; with the option's key it makes the patch key `technique.key`. */
  technique: string;
  option: OptionContent;
  /** The technique's option values from the host. */
  values: Record<string, unknown>;
  choices: SettingsChoices;
  /** What the host says is wrong with this option, if anything. */
  error: string | null;
  onUpdate: (patch: SettingsPatch) => void;
  onPickKey: () => void;
}

const asText = (value: unknown) => (typeof value === "string" ? value : "");
const asNumber = (value: unknown) => (typeof value === "number" ? value : 0);
const asList = (value: unknown) => (Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : []);

/** One option of a technique: the control for its kind, with its help always shown beneath it. */
export default function OptionControl({ technique, option, values, choices, error, onUpdate, onPickKey }: OptionControlProps) {
  const patchKey = (key: string) => `${technique}.${key}`;
  const set = (key: string, value: boolean | number | string | string[]) => onUpdate({ [patchKey(key)]: value });

  switch (option.type) {
    case "select":
      return <SelectOption option={option} value={asText(values[option.key])} choices={choices} error={error} onChange={(v) => set(option.key, v)} />;
    case "number":
      return <NumberOption option={option} value={asNumber(values[option.key])} error={error} onChange={(v) => set(option.key, v)} />;
    case "range":
      return <RangeOption option={option} value={asNumber(values[option.key])} error={error} onChange={(v) => set(option.key, v)} />;
    case "check":
      return <CheckOption option={option} checked={values[option.key] === true} error={error} onChange={(v) => set(option.key, v)} />;
    case "group":
      return <GroupOption option={option} values={values} onChange={(key, v) => set(key, v)} />;
    case "text":
      return <TextOption option={option} value={asText(values[option.key])} error={error} onCommit={(v) => set(option.key, v)} />;
    case "file":
      return <TextOption option={option} value={asText(values[option.key])} error={error} onCommit={(v) => set(option.key, v)} onBrowse={onPickKey} />;
    case "list":
      return <ListOption option={option} value={asList(values[option.key])} error={error} onCommit={(v) => set(option.key, v)} />;
  }
}

interface SelectOptionProps {
  option: OptionContent;
  value: string;
  choices: SettingsChoices;
  error: string | null;
  onChange: (value: string) => void;
}

/** A drop-down whose help explains the value it shows now. */
function SelectOption({ option, value, choices, error, onChange }: SelectOptionProps) {
  const set = option.choiceSet ? content.choiceSets[option.choiceSet] : undefined;
  const offered = set ? choices[set.source] : [];
  // Without a list from the host, offer what the content knows; keep a value the list does not have, so the box is never blank.
  const values = offered.length > 0 ? [...offered] : set ? Object.keys(set.values) : [];
  if (value && !values.includes(value)) values.push(value);
  const help = set?.values[value]?.help ?? option.help;

  return (
    <div className="opt">
      <Field label={option.label} help={help} error={error}>
        {(control) => (
          <div className="ctl">
            <Select {...control} value={value} options={values.map((v) => ({ value: v, label: set?.values[v]?.label ?? v }))} onChange={onChange} />
          </div>
        )}
      </Field>
    </div>
  );
}

interface NumberOptionProps {
  option: OptionContent;
  value: number;
  error: string | null;
  onChange: (value: number) => void;
}

/** A number box: what is typed is kept while the box has focus, and a number is sent as soon as there is one. */
function NumberOption({ option, value, error, onChange }: NumberOptionProps) {
  const [draft, setDraft] = useState<string | null>(null);
  return (
    <div className="opt">
      <Field label={option.label} help={option.help} error={error}>
        {(control) => (
          <div className="ctl">
            <input
              {...control}
              type="number"
              className="field mono num"
              min={option.min}
              max={option.max}
              value={draft ?? String(value)}
              onChange={(e) => {
                const text = e.target.value;
                setDraft(text);
                if (text.trim() !== "" && Number.isFinite(Number(text))) onChange(Number(text));
              }}
              onBlur={() => setDraft(null)}
            />
            {option.unit && <span className="help">{option.unit}</span>}
          </div>
        )}
      </Field>
    </div>
  );
}

/** A slider that keeps its own position while it is dragged, so the answers coming back never pull it around. */
function RangeOption({ option, value, error, onChange }: NumberOptionProps) {
  const [live, setLive] = useState<number | null>(null);
  return (
    <div className="opt" onBlur={() => setLive(null)}>
      <Field label={option.label} help={option.help} error={error}>
        {(control) => (
          <div className="ctl">
            <Slider
              {...control}
              min={option.min}
              max={option.max}
              value={live ?? value}
              onChange={(next) => {
                setLive(next);
                onChange(next);
              }}
            />
          </div>
        )}
      </Field>
    </div>
  );
}

interface CheckOptionProps {
  option: OptionContent;
  checked: boolean;
  error: string | null;
  onChange: (checked: boolean) => void;
}

/** A tick box with its label beside it and its help beneath. */
function CheckOption({ option, checked, error, onChange }: CheckOptionProps) {
  const id = useId();
  const describedBy = [option.help ? `${id}-help` : null, error ? `${id}-error` : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className="opt">
      <div className="ctl">
        <label className="lab">
          <input
            type="checkbox"
            className="chk"
            checked={checked}
            aria-describedby={describedBy}
            onChange={(e) => onChange(e.target.checked)}
          />{" "}
          {option.label}
        </label>
      </div>
      {option.help && (
        <p className="help" id={`${id}-help`}>
          {option.help}
        </p>
      )}
      {error && (
        <p className="err" id={`${id}-error`}>
          {error}
        </p>
      )}
    </div>
  );
}

interface GroupOptionProps {
  option: OptionContent;
  values: Record<string, unknown>;
  onChange: (key: string, checked: boolean) => void;
}

/** A row of tick boxes under one label and one help text. */
function GroupOption({ option, values, onChange }: GroupOptionProps) {
  const id = useId();
  return (
    <div className="opt" role="group" aria-labelledby={`${id}-label`} aria-describedby={option.help ? `${id}-help` : undefined}>
      <span className="lab" id={`${id}-label`}>
        {option.label}
      </span>
      <div className="cgrid">
        {(option.members ?? []).map((member) => (
          <label key={member.key}>
            <input
              type="checkbox"
              className="chk"
              checked={values[member.key] === true}
              onChange={(e) => onChange(member.key, e.target.checked)}
            />{" "}
            {member.label}
          </label>
        ))}
      </div>
      {option.help && (
        <p className="help" id={`${id}-help`}>
          {option.help}
        </p>
      )}
    </div>
  );
}

interface TextOptionProps {
  option: OptionContent;
  value: string;
  error: string | null;
  onCommit: (value: string) => void;
  /** A Browse… button beside the box, for an option that names a file. */
  onBrowse?: () => void;
}

/** A text box that follows the host's value and sends what was typed when it is left, or on Enter. */
function TextOption({ option, value, error, onCommit, onBrowse }: TextOptionProps) {
  const [text, setText] = useDraft(value);
  const commit = () => {
    if (text !== value) onCommit(text);
  };
  return (
    <div className="opt">
      <Field label={option.label} help={option.help} error={error} required={option.required}>
        {(control) => (
          <div className={onBrowse ? "rowfield" : "ctl"}>
            <input
              {...control}
              type="text"
              className="field mono"
              aria-required={option.required ? true : undefined}
              placeholder={option.placeholder}
              value={text}
              onChange={(e) => setText(e.target.value)}
              onBlur={commit}
              onKeyDown={(e: KeyboardEvent) => {
                if (e.key === "Enter") commit();
              }}
            />
            {onBrowse && (
              <button type="button" className="btn" onClick={onBrowse}>
                {strings.protection.browse}
              </button>
            )}
          </div>
        )}
      </Field>
    </div>
  );
}

interface ListOptionProps {
  option: OptionContent;
  value: string[];
  error: string | null;
  onCommit: (value: string[]) => void;
}

/** A box of patterns, one per line; the host trims, drops blanks and duplicates. Sent when the box is left. */
export function ListOption({ option, value, error, onCommit }: ListOptionProps) {
  const joined = value.join("\n");
  const [text, setText] = useDraft(joined);
  return (
    <div className="opt">
      <Field label={option.label} help={option.help} error={error}>
        {(control) => (
          <textarea
            {...control}
            className="field mono"
            rows={Math.max(2, Math.min(6, value.length + 1))}
            placeholder={option.placeholder}
            value={text}
            onChange={(e) => setText(e.target.value)}
            onBlur={() => {
              if (text !== joined) onCommit(text.split(/\r?\n/));
            }}
          />
        )}
      </Field>
    </div>
  );
}
