import raw from "./techniques.json";

// The human copy for the Protection screen: what each technique is called, what it does, and what each option means.
// The ids are the ids of the host's technique list (Obfy.App ProtectionSession), so a technique with no entry here is
// still shown, with its id as its name. TechniqueContentContractTests (Obfy.Interop.Tests) pins the ids and option keys to the host's.

export type OptionType = "select" | "number" | "range" | "check" | "text" | "list" | "file" | "group";

export interface ChoiceText {
  label: string;
  help: string;
}

/** The labels and help for the values of one dropdown; `source` names the list in `SettingsSnapshot.choices` that offers them. */
export interface ChoiceSet {
  source: "algorithms" | "controlFlowModes" | "namingModes" | "packingRids";
  values: Record<string, ChoiceText>;
}

export interface OptionContent {
  /** The option's name in the host's patch key `technique.key`. */
  key: string;
  type: OptionType;
  label: string;
  /** Always shown beneath the control. A select shows the help of the chosen value instead, when it has one. */
  help?: string;
  choiceSet?: string;
  min?: number;
  max?: number;
  unit?: string;
  placeholder?: string;
  required?: boolean;
  /** For a `group`: the checkboxes, each an option of the technique. */
  members?: { key: string; label: string }[];
}

export interface TechniqueContent {
  group: string;
  name: string;
  summary: string;
  what: string;
  good: string[];
  securityNote?: { title: string; text: string };
  beforeAfter?: { before: string; after: string };
  windowsOnly?: boolean;
  /** For a technique with no switch of its own: the options that the switch turns on, and the ones it turns off. */
  switch?: { on: string[]; off: string[] };
  guideTopic: string;
  options: OptionContent[];
}

export interface ScopeListContent {
  set: "exclusions" | "inclusions";
  key: "namespaces" | "types" | "methods";
  label: string;
  placeholder: string;
  help: string;
}

export interface ScopeContent {
  group: string;
  name: string;
  summary: string;
  what: string;
  good: string[];
  guideTopic: string;
  lists: ScopeListContent[];
}

interface TechniquesFile {
  groups: string[];
  runtimeProfiles: Record<string, string>;
  choiceSets: Record<string, ChoiceSet>;
  techniques: Record<string, TechniqueContent>;
  scope: ScopeContent;
}

export const content = raw as TechniquesFile;

/** The id of the row that stands for the exclusion and inclusion lists, which the host does not list as a technique. */
export const SCOPE_ID = "scope";
