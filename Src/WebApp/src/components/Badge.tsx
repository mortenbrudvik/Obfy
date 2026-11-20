import type { ReactNode } from "react";
import "./components.css";

interface BadgeProps {
  tone?: "neutral" | "accent" | "warn";
  children: ReactNode;
}

/** A small label next to a name ("Recommended", "Windows only"). */
export default function Badge({ tone = "neutral", children }: BadgeProps) {
  return <span className={tone === "neutral" ? "badge" : `badge ${tone}`}>{children}</span>;
}
