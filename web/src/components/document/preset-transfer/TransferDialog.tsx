import { useLayoutEffect, type ReactNode } from "react";
import { useActionDialog } from "../../ui/useActionDialog";
import "./preset-transfer.css";

export function TransferDialog({ title, busy, trigger, onDismiss, children }: {
  title: string; busy: boolean; trigger: HTMLElement; onDismiss: () => void; children: ReactNode;
}) {
  const modal = useActionDialog(busy);
  const { dialog, heading, openDialog } = modal;
  useLayoutEffect(() => {
    openDialog();
    const nativeDialog = dialog.current;
    return () => { nativeDialog?.close(); trigger.focus(); };
    // Open once per mounted transfer; request state must not reopen/refocus the dialog.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return <dialog ref={dialog} className="preset-transfer-dialog" aria-labelledby="preset-transfer-title"
    onCancel={event => { event.preventDefault(); if (!busy) onDismiss(); }}>
    <h3 ref={heading} id="preset-transfer-title" tabIndex={-1}>{title}</h3>
    {children}
  </dialog>;
}
