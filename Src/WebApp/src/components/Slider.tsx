import "./components.css";

interface SliderProps {
  /** The accessible name, unless a visible label points at `id`. */
  label?: string;
  value: number;
  min?: number;
  max?: number;
  step?: number;
  onChange: (value: number) => void;
  disabled?: boolean;
  id?: string;
  "aria-describedby"?: string;
}

/** A range input with its current value beside it. */
export default function Slider({
  label,
  value,
  min = 0,
  max = 100,
  step = 1,
  onChange,
  disabled,
  id,
  "aria-describedby": describedBy,
}: SliderProps) {
  return (
    <div className="slider">
      <input
        type="range"
        id={id}
        aria-label={label}
        aria-describedby={describedBy}
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(Number(e.target.value))}
      />
      <output aria-hidden="true">{value}</output>
    </div>
  );
}
