import { useRef, useState } from "react";
import { api, ApiError } from "../../api/client";
import { saveBlob } from "../../lib/saveBlob";

/** Downloads the current bytes of one save file from the in-browser runtime. */
export function DownloadFileButton({ workspaceId, documentId, fallbackName }: { workspaceId: string; documentId: string; fallbackName: string }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // Synchronous guard: a second activation can arrive before React re-renders with `busy`.
  const running = useRef(false);
  const download = async () => {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    try {
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
    <button type="button" className="btn btn--small btn--quiet" aria-disabled={busy || undefined} onClick={() => void download()}>
      {busy ? "Preparing download…" : "Download file"}
    </button>
    {error && <span role="alert" className="field__error">{error}</span>}
  </>;
}
