import { content, type TechniqueContent } from "../../content/content";
import { strings } from "../../strings";

/** The words for a technique the content does not know yet: its id stands in for the name. */
export function contentFor(id: string): TechniqueContent {
  return (
    content.techniques[id] ?? {
      group: strings.protection.otherGroup,
      name: id,
      summary: "",
      what: "",
      good: [],
      guideTopic: "tech",
      options: [],
    }
  );
}
