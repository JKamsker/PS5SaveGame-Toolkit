import { useEffect, useRef } from "react";
import type { ToolkitPreset } from "../../api/protocol";

export function PresetDeleteDialog({ preset, onDelete, onCancel }: { preset: ToolkitPreset; onDelete: () => void; onCancel: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const node = dialog.current;
    if (!node) return;
    if (!node.open) {
      if (typeof node.showModal === "function") node.showModal(); else node.setAttribute("open", "");
      cancel.current?.focus();
    }
    return () => { if (node.open && typeof node.close === "function") node.close(); };
  }, []);
  return <dialog ref={dialog} aria-labelledby="preset-delete-title" className="presets-delete" onCancel={event => { event.preventDefault(); onCancel(); }}><h3 id="preset-delete-title">Delete “{preset.name}”?</h3><p>This removes the external preset record. Save bytes are unchanged.</p><div className="bar__actions"><button ref={cancel} type="button" className="btn" onClick={onCancel}>Cancel</button><button type="button" className="btn btn--danger" onClick={onDelete}>Delete preset</button></div></dialog>;
}
