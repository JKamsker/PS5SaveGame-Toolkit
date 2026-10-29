import type { MouseEvent } from "react";

/** The clear button disappears; keep keyboard focus inside its own table. */
export function clearTableSelection(event: MouseEvent<HTMLButtonElement>, clear: () => void) {
  const trigger = event.currentTarget;
  if (document.activeElement === trigger) {
    const table = trigger.closest(".table");
    const header = table?.querySelector<HTMLInputElement>('[role="columnheader"] input[type="checkbox"]');
    header?.focus();
    // A zero-match page disables the header. The heading (or untitled scroller) remains usable.
    if (document.activeElement !== header) table?.querySelector<HTMLElement>(".table__title, .table__scroller[tabindex]")?.focus();
  }
  clear();
}
