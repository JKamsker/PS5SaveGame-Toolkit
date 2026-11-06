import { useContext, useRef, useState } from "react";
import { api, ApiError } from "../../api/client";
import { saveBlob } from "../../lib/saveBlob";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";

/**
 * A field draft still holding focus commits on blur. Browsers blur it before a pointer or keyboard
 * activation reaches this button, but a programmatic or assistive activation may not: blur it here so
 * its edit is tracked before the download waits for pending mutations.
 */
function commitFocusedDraft(button: HTMLElement) {
  const focused = button.ownerDocument.activeElement;
  if (focused !== button && (focused instanceof HTMLInputElement || focused instanceof HTMLTextAreaElement
    || focused instanceof HTMLSelectElement)) focused.blur();
}

/** Downloads the current bytes of one save file from the in-browser runtime. */
export function DownloadFileButton({ workspaceId, documentId, fallbackName }: { workspaceId: string; documentId: string; fallbackName: string }) {
  const activity = useContext(WorkspaceActivityContext);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // Synchronous guard: a second activation can arrive before React re-renders with `busy`.
  const running = useRef(false);
  const download = async (button: HTMLElement) => {
    if (running.current) return;
    running.current = true;
    commitFocusedDraft(button);
    setBusy(true);
    setError("");
    try {
      // Like an export: the bytes must include every edit accepted before this click, also queued ones.
      if (activity) await activity.waitForMutations();
      const { blob, name } = await api.download(workspaceId, documentId);
      saveBlob(blob, name ?? fallbackName.split("/").pop() ?? "save.bin");
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "The download failed. Try again.");
    } finally {
      running.current = false;
      setBusy(false);
    }
  };
  // aria-disabled rather than disabled: a disabled button drops keyboard focus to <body> mid-download.
  return <>
    <button type="button" className="btn btn--small btn--quiet" aria-disabled={busy || undefined} onClick={event => void download(event.currentTarget)}>
      {busy ? "Preparing download…" : "Download file"}
    </button>
    {error && <span role="alert" className="field__error">{error}</span>}
  </>;
}
