import { lazy, Suspense, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { SaveDocument } from "../../../api/protocol";

const ImportPresetDialog = lazy(() => import("./ImportPresetDialog"));
const ExportPresetDialog = lazy(() => import("./ExportPresetDialog"));

export function PresetTransferToolbar({ document }: { document: SaveDocument }) {
  const client = useQueryClient();
  const [dialog, setDialog] = useState<{ kind: "import" | "export"; trigger: HTMLElement }>();
  const [message, setMessage] = useState<string>();
  const imported = (text: string) => { setMessage(text); void client.invalidateQueries({ queryKey: ["presets"] }); };
  return <div className="preset-transfer-toolbar">
    <div className="bar__actions"><button type="button" className="btn btn--small" onClick={event => { setMessage(undefined); setDialog({ kind: "import", trigger: event.currentTarget }); }}>Import presets</button>
      <button type="button" className="btn btn--small" onClick={event => { setMessage(undefined); setDialog({ kind: "export", trigger: event.currentTarget }); }}>Export presets</button></div>
    {message && <p role="status">{message}</p>}
    <Suspense fallback={<p role="status">Opening preset transfer…</p>}>
      {dialog?.kind === "import" && <ImportPresetDialog document={document} trigger={dialog.trigger} onDismiss={() => setDialog(undefined)} onImported={imported} />}
      {dialog?.kind === "export" && <ExportPresetDialog trigger={dialog.trigger} onDismiss={() => setDialog(undefined)} onDownloaded={setMessage} />}
    </Suspense>
  </div>;
}
