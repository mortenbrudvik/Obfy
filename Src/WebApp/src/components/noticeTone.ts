import type { NoticeLevel } from "../types/domain";

export const noticeTone: Record<NoticeLevel, "info" | "warn" | "error"> = {
  Info: "info",
  Warning: "warn",
  Error: "error",
};
