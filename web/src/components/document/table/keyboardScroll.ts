import type { KeyboardEvent } from "react";

/** Must match `.table__cell` height in table.css. Detail rows are measured. */
export const ROW_HEIGHT = 41;

/** Scroll the table viewport itself; leave editing and selection keys to its children. */
export function scrollTableByKey(event: KeyboardEvent<HTMLDivElement>, totalHeight: number, rowHeight: number) {
  if (event.target !== event.currentTarget || event.altKey || event.ctrlKey || event.metaKey) return;
  const viewport = event.currentTarget;
  let top = viewport.scrollTop;
  switch (event.key) {
    case "ArrowDown": top += rowHeight; break;
    case "ArrowUp": top -= rowHeight; break;
    case "PageDown": top += viewport.clientHeight; break;
    case "PageUp": top -= viewport.clientHeight; break;
    case "Home": top = 0; break;
    case "End": top = totalHeight; break;
    default: return;
  }
  event.preventDefault();
  viewport.scrollTop = Math.max(0, Math.min(top, totalHeight - viewport.clientHeight));
}
