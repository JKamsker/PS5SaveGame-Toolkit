import { useEffect, useRef } from "react";
import { useDocumentState, type DocumentStore } from "../../state/documentStore";
import { useAsyncTask } from "../ui/useAsyncTask";

export function HistoryButtons({ store, onUndo, onRedo }: { store: DocumentStore; onUndo: () => Promise<unknown>; onRedo: () => Promise<unknown> }) {
  const { state } = useDocumentState(store);
  const { busy, run } = useAsyncTask(store);
  const controls = useRef<HTMLDivElement>(null);
  const execute = (action: () => Promise<unknown>) => run(async () => {
    // Native disabled buttons lose focus. Use the persistent document title during history work.
    if (document.activeElement && controls.current?.contains(document.activeElement)) {
      controls.current.closest(".editor__head")?.querySelector<HTMLElement>(".editor__title")?.focus();
    }
    await action();
  });
  const undo = () => execute(onUndo);
  const redo = () => execute(onRedo);
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (document.querySelector("dialog[open]")) return;
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) return;
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "z") return;
      e.preventDefault();
      if (e.shiftKey) {
        if (store.state.canRedo) void redo();
      } else if (store.state.canUndo) void undo();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  });

  return (
    <div ref={controls} className="bar__actions">
      <button type="button" className="btn btn--small" disabled={busy || !state.canUndo} onClick={() => void undo()} title="Undo (Ctrl+Z)">
        Undo
      </button>
      <button type="button" className="btn btn--small" disabled={busy || !state.canRedo} onClick={() => void redo()} title="Redo (Ctrl+Shift+Z)">
        Redo
      </button>
    </div>
  );
}
