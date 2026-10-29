import { useEffect, useRef, useState } from "react";

type FocusTarget = "heading" | "parameters" | "error" | "trigger";
const controls = "input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)";

/** Keep native modal behavior while handling controls replaced by async action results. */
export function useActionDialog(busy: boolean) {
  const dialog = useRef<HTMLDialogElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [focusTarget, requestFocus] = useState<FocusTarget | null>(null);

  useEffect(() => {
    if (busy || !focusTarget) return;
    if (!dialog.current) {
      requestFocus(null);
      return;
    }
    if (focusTarget === "trigger" || !dialog.current?.open) {
      trigger.current?.focus();
    } else {
      const root = dialog.current;
      const field = focusTarget === "error" ? root.querySelector('[data-field-error="true"]') : null;
      const target = field?.querySelector<HTMLElement>(controls)
        ?? (focusTarget === "error" ? root.querySelector<HTMLElement>('[role="alert"]') : null)
        ?? (focusTarget === "parameters" ? root.querySelector<HTMLElement>(controls) : null)
        ?? heading.current;
      target?.focus();
    }
    requestFocus(null);
  }, [busy, focusTarget]);

  const focusHeading = () => { if (dialog.current?.open) heading.current?.focus(); };
  return {
    dialog, heading, trigger, requestFocus, focusHeading,
    openDialog: () => { dialog.current?.showModal(); focusHeading(); },
    closeDialog: () => {
      if (!dialog.current?.open) return;
      dialog.current.close();
      requestFocus("trigger");
    },
    onClose: () => requestFocus("trigger"),
  };
}
