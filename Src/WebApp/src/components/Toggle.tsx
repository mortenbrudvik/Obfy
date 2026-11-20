import "./components.css";

interface ToggleProps {
  /** The accessible name; the switch has no visible text of its own. */
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  id?: string;
  /** A stable hook for end-to-end tests. */
  testId?: string;
  "aria-describedby"?: string;
}

/** An on/off switch: a real checkbox with the switch role. */
export default function Toggle({ label, checked, onChange, disabled, id, testId, "aria-describedby": describedBy }: ToggleProps) {
  return (
    <input
      type="checkbox"
      role="switch"
      className="sw"
      id={id}
      data-testid={testId}
      aria-label={label}
      aria-describedby={describedBy}
      checked={checked}
      disabled={disabled}
      onChange={(e) => onChange(e.target.checked)}
    />
  );
}
