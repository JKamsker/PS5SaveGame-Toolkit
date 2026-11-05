import { useState } from "react";
import { api, ApiError } from "../../api/client";
import { saveBlob } from "../../lib/saveBlob";

/** Downloads the current bytes of one save file from the in-browser runtime. */
export function DownloadFileButton({ workspaceId, documentId, fallbackName }: { workspaceId: string; documentId: string; fallbackName: string }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const download = async () => {
    setBusy(true);
    setError("");
    try {
      const { blob, name } = await api.download(workspaceId, documentId);
      saveBlob(blob, name ?? fallbackName.split("/").pop() ?? "save.bin");
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "The download failed. Try again.");
    } finally {
      setBusy(false);
    }
  };
  return <>
    <button type="button" className="btn btn--small btn--quiet" disabled={busy} onClick={() => void download()}>
      {busy ? "Preparing download…" : "Download file"}
    </button>
    {error && <span role="alert" className="field__error">{error}</span>}
  </>;
}
