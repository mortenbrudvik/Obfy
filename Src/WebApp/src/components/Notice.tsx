import type { ReactNode } from "react";
import "./components.css";

interface NoticeProps {
  /** `info` is a quiet hint; `warn` is a caution with a bold title; `error` is spoken at once. */
  tone?: "info" | "warn" | "error";
  title?: string;
  children: ReactNode;
}

/** A message set apart from the content around it. */
export default function Notice({ tone = "info", title, children }: NoticeProps) {
  if (tone === "info") {
    return (
      <div className="info">
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <circle cx="8" cy="8" r="6.5" />
          <path d="M8 7v4M8 4.8v.2" />
        </svg>
        <span>
          {title && <b>{title} </b>}
          {children}
        </span>
      </div>
    );
  }
  return (
    <div className={tone === "error" ? "note error" : "note"} role={tone === "error" ? "alert" : undefined}>
      {title && <b>{title}</b>}
      {children}
    </div>
  );
}
