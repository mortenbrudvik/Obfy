/** The id of a tab button, so a panel can name it with aria-labelledby. */
export function tabId(prefix: string, id: string): string {
  return `${prefix}-tab-${id}`;
}

/** The props a tab panel needs to belong to the tab `selected` of the `Tabs` with the same `idPrefix`. */
export function tabPanelProps(prefix: string, selected: string) {
  return { id: `${prefix}-panel`, role: "tabpanel" as const, "aria-labelledby": tabId(prefix, selected) };
}
