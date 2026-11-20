import { useId, type ReactNode } from "react";
import "./components.css";

/** What a Field hands to its control so that the label, the help and the error belong to it. */
export interface FieldControlProps {
  id: string;
  "aria-describedby": string;
  "aria-invalid"?: true;
}

interface FieldProps {
  label: string;
  /** Always visible, beneath the control. */
  help?: string;
  /** What is wrong with the value; the control is marked invalid. */
  error?: string | null;
  /** The value must be given: "(required)" follows the label. */
  required?: boolean;
  children: (control: FieldControlProps) => ReactNode;
}

/** A labelled control with its help text always shown beneath it. */
export default function Field({ label, help, error, required, children }: FieldProps) {
  const id = useId();
  const helpId = `${id}-help`;
  const errorId = `${id}-error`;
  const describedBy = [help ? helpId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className="fl">
      <label htmlFor={id}>
        {label}
        {required && <span className="help"> (required)</span>}
      </label>
      {children({ id, "aria-describedby": describedBy, ...(error ? { "aria-invalid": true as const } : {}) })}
      {help && (
        <p className="help" id={helpId}>
          {help}
        </p>
      )}
      {error && (
        <p className="err" id={errorId}>
          {error}
        </p>
      )}
    </div>
  );
}
