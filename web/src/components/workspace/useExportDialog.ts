import { useContext, useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { ExportScope } from "../../api/protocol";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";
import { useAsyncTask } from "../ui/useAsyncTask";

interface ExportRequest {
  workspaceId: string;
  open: boolean;
  onClose: () => void;
  onToast: (message: string, isError?: boolean) => void;
}

function useExportDownload({ workspaceId, open, onClose, onToast }: ExportRequest, scope: ExportScope, mirror: boolean) {
  const activity = useContext(WorkspaceActivityContext);
  const { busy, run: submit } = useAsyncTask(`${workspaceId}:${open}`);
  const [downloadError, setDownloadError] = useState("");
  // eslint-disable-next-line react-hooks/set-state-in-effect -- Preserve the existing modal-open reset, including retained download errors while closed.
  useEffect(() => { if (open) setDownloadError(""); }, [open]);
  const download = () => submit(async isCurrent => {
    if (!open) return;
    setDownloadError("");
    try {
      if (activity) await activity.waitForMutations();
      if (!isCurrent()) return;
      const { blob, name } = await api.exportZip(workspaceId, scope, mirror);
      if (!isCurrent()) return;
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = name;
      a.click();
      URL.revokeObjectURL(url);
      onToast(`Exported ${name}`);
      onClose();
    } catch (error) {
      if (!isCurrent()) return;
      const message = error instanceof ApiError ? error.message : "Export failed.";
      setDownloadError(message);
      onToast(message, true);
    }
  });
  return { busy, downloadError, download };
}

export function useExportDialog(props: ExportRequest) {
  const { workspaceId, open } = props;
  const [scope, setScope] = useState<ExportScope>("ChangedDirectories");
  const [mirror, setMirror] = useState(true);
  const activity = useContext(WorkspaceActivityContext);
  const download = useExportDownload(props, scope, mirror);
  const plan = useQuery({
    queryKey: ["export-plan", workspaceId, scope, mirror],
    queryFn: async () => {
      if (activity) await activity.waitForMutations();
      return api.exportPlan(workspaceId, scope, mirror);
    },
    enabled: open,
    // Edits can occur while this mounted dialog is closed, inside the global freshness window.
    staleTime: 0,
    retry: false,
  });
  return { scope, setScope, mirror, setMirror, plan, ...download };
}
