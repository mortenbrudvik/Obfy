import "./components.css";

interface MeterProps {
  label: string;
  /** 0 to 3 filled steps. */
  value: number;
}

/** A three-step meter (protection, run-time cost, compatibility risk). */
export default function Meter({ label, value }: MeterProps) {
  return (
    <div className="meter">
      <span>{label}</span>
      <i role="img" aria-label={`${value} of 3`}>
        {[1, 2, 3].map((step) => (
          <s key={step} className={step <= value ? "on" : undefined} />
        ))}
      </i>
    </div>
  );
}
