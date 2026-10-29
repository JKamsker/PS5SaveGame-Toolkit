import { useCallback, useRef } from "react";

/** Focus late-arriving route content once, without interrupting background refreshes. */
export function useEntryFocus(entryKey: string) {
  const focusedEntry = useRef<string | null>(null);
  return useCallback((element: HTMLElement | null) => {
    if (!element || focusedEntry.current === entryKey) return;
    element.focus();
    focusedEntry.current = entryKey;
  }, [entryKey]);
}
