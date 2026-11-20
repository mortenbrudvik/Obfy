import { useId, type KeyboardEvent } from "react";
import { tabId } from "./tabIds";
import "./components.css";

export interface TabItem {
  id: string;
  label: string;
}

interface TabsProps {
  /** The accessible name of the tab list. */
  label: string;
  tabs: readonly TabItem[];
  selected: string;
  onChange: (id: string) => void;
  /** Ties each tab to its panel (see `tabPanelProps`); generated when left out. */
  idPrefix?: string;
}

/** A row of tabs: one tab stop; the arrow keys, Home and End move between them. */
export default function Tabs({ label, tabs, selected, onChange, idPrefix }: TabsProps) {
  const generated = useId();
  const prefix = idPrefix ?? generated;

  const move = (event: KeyboardEvent, index: number) => {
    const last = tabs.length - 1;
    const target =
      event.key === "ArrowRight" ? (index === last ? 0 : index + 1)
      : event.key === "ArrowLeft" ? (index === 0 ? last : index - 1)
      : event.key === "Home" ? 0
      : event.key === "End" ? last
      : null;
    if (target === null) return;
    event.preventDefault();
    onChange(tabs[target].id);
    document.getElementById(tabId(prefix, tabs[target].id))?.focus();
  };

  return (
    <div className="tabs" role="tablist" aria-label={label}>
      {tabs.map((tab, index) => {
        const isSelected = tab.id === selected;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            className="tab"
            id={tabId(prefix, tab.id)}
            aria-selected={isSelected}
            aria-controls={`${prefix}-panel`}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => onChange(tab.id)}
            onKeyDown={(event) => move(event, index)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}
