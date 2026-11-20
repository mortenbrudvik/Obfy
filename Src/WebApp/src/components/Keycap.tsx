import "./components.css";

/** A key as it appears on the keyboard: `<Keycap>Ctrl</Keycap>`. */
export default function Keycap({ children }: { children: string }) {
  return <kbd>{children}</kbd>;
}
