import { forwardRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { ExportScope } from "../../api/protocol";
import { formatBytes } from "../../lib/values";

const scopes: { value: ExportScope; label: string; hint: string }[] = [
  { value: "ChangedDirectories", label: "Changed save folders", hint: "Complete folders including sce_sys, ready to restore with your save manager." },
  { value: "ChangedFiles", label: "Only changed files", hint: "Just the rewritten files, same paths as the import." },
  { value: "Everything", label: "Whole dump", hint: "Every imported file, with your edits applied." },
];

interface ExportDialogProps {
  workspaceId: string;
  open: boolean;
  onClose: () => void;
  onToast: (message: string, isError?: boolean) => void;
}

export const ExportDialog = forwardRef<HTMLDialogElement, ExportDialogProps>(function ExportDialog({ workspaceId, open, onClose, onToast }, ref) {
  const [scope, setScope] = useState<ExportScope>("ChangedDirectories");
  const [mirror, setMirror] = useState(true);
  const [busy, setBusy] = useState(false);
  const plan = useQuery({
    queryKey: ["export-plan", workspaceId, scope, mirror],
    queryFn: () => api.exportPlan(workspaceId, scope, mirror),
    enabled: open,
    retry: false,
  });

  const download = async () => {
    setBusy(true);
    try {
      const { blob, name } = await api.exportZip(workspaceId, scope, mirror);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = name;
      a.click();
      URL.revokeObjectURL(url);
      onToast(`Exported ${name}`);
      onClose();
    } catch (error) {
      onToast(error instanceof ApiError ? error.message : "Export failed.", true);
    } finally {
      setBusy(false);
    }
  };

  const changed = plan.data?.files.filter((f) => f.reason !== "unchanged") ?? [];
  return (
    <dialog ref={ref} className="dialog" onClose={onClose} aria-labelledby="export-title">
      <div className="dialog__body">
        <h2 className="dialog__title" id="export-title">
          Export edited saves
        </h2>
        <p className="section__description">Every edited file is re-opened by its plugin before it is packed. Keep your original dump as a backup.</p>
        <fieldset style={{ border: 0, padding: 0, margin: "0 0 16px" }}>
          <legend className="visually-hidden">What to include</legend>
          {scopes.map((s) => (
            <label key={s.value} style={{ display: "grid", gridTemplateColumns: "auto 1fr", gap: "2px 10px", padding: "6px 0" }}>
              <input type="radio" name="scope" checked={scope === s.value} onChange={() => setScope(s.value)} />
              <strong style={{ fontWeight: 580 }}>{s.label}</strong>
              <span className="field__hint" style={{ gridColumn: 2 }}>
                {s.hint}
              </span>
            </label>
          ))}
        </fieldset>
        <label style={{ display: "flex", gap: 10, alignItems: "center", marginBottom: 16 }}>
          <input type="checkbox" checked={mirror} onChange={(e) => setMirror(e.target.checked)} />
          Also update the console backup copy (sce_bu) when it matched the original
        </label>
        {plan.isError && <p className="notice notice--Danger">{plan.error.message}</p>}
        {plan.data && (
          <div className="notice notice--Info">
            {plan.data.changedCount === 0 ? (
              "Nothing has been changed yet."
            ) : (
              <>
                {changed.length} rewritten, {plan.data.files.length} files in total
                <ul style={{ margin: "8px 0 0", paddingLeft: 18 }}>
                  {changed.slice(0, 8).map((f) => (
                    <li key={f.relativePath}>
                      <span className="mono">{f.relativePath}</span> — {f.reason}, {formatBytes(f.length)}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        )}
      </div>
      <div className="dialog__footer">
        <button type="button" className="btn btn--quiet" onClick={onClose}>
          Cancel
        </button>
        <button type="button" className="btn btn--primary" disabled={busy || !plan.data || (plan.data.changedCount === 0 && scope !== "Everything")} onClick={() => void download()}>
          {busy ? "Packing…" : "Download zip"}
        </button>
      </div>
    </dialog>
  );
});
