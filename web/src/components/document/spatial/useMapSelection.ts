import { useRef, useState, type MouseEvent } from "react";

/** Both diagram markers and text-list buttons can open the same detail panel. */
export function useMapSelection() {
  const [selected, setSelected] = useState<string | null>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const selectMarker = (id: string, button: HTMLButtonElement) => {
    trigger.current = button;
    setSelected(id);
  };
  const clearSelection = (event: MouseEvent<HTMLButtonElement>) => {
    if (document.activeElement === event.currentTarget) {
      trigger.current?.focus();
      // Connected controls can still be hidden inside a collapsed text alternative.
      if (document.activeElement !== trigger.current) heading.current?.focus();
    }
    setSelected(null);
  };
  return { selected, heading, selectMarker, clearSelection };
}
