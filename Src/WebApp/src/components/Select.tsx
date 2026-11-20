import "./components.css";

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  /** The accessible name, unless a visible label points at `id`. */
  label?: string;
  value: string;
  options: readonly SelectOption[];
  onChange: (value: string) => void;
  disabled?: boolean;
  id?: string;
  "aria-describedby"?: string;
  "aria-invalid"?: boolean;
}

/** A drop-down: a real select. */
export default function Select({
  label,
  value,
  options,
  onChange,
  disabled,
  id,
  "aria-describedby": describedBy,
  "aria-invalid": invalid,
}: SelectProps) {
  return (
    <select
      className="field"
      id={id}
      aria-label={label}
      aria-describedby={describedBy}
      aria-invalid={invalid}
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
    >
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}
