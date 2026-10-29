import { useCallback, useMemo, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { SaveFileInfo } from "../../api/protocol";

export function useOpenDocument(workspaceId: string, beginSelection: () => () => boolean,
  select: (documentId: string) => void, notify: (message: string, error?: boolean) => void) {
  const client = useQueryClient();
  const lanes = useMemo(() => new Map<string, Promise<void>>(), [workspaceId]);
  const pending = useRef<{ key: string; isCurrent: () => boolean } | null>(null);

  return useCallback(async (file: SaveFileInfo, pluginId?: string) => {
    const key = JSON.stringify([workspaceId, file.id, pluginId]);
    if (pending.current?.key === key && pending.current.isCurrent()) return;
    const isCurrent = beginSelection();
    pending.current = { key, isCurrent };
    if (file.openDocumentId && !pluginId) {
      select(file.openDocumentId);
      pending.current = null;
      return;
    }
    // Replacement opens mutate file ownership. Finish the earlier open before starting another
    // plugin for this file, while independent files can still open concurrently.
    const request = (lanes.get(file.id) ?? Promise.resolve()).then(async () => {
      if (!isCurrent()) return;
      try {
        const doc = await api.open(workspaceId, file.id, pluginId);
        void client.invalidateQueries({ queryKey: ["workspace", workspaceId] });
        if (!isCurrent()) return;
        client.setQueryData(["document", workspaceId, doc.id], doc);
        select(doc.id);
      } catch (error) {
        if (isCurrent()) notify(error instanceof ApiError ? error.message : "Could not open the file.", true);
      }
    });
    lanes.set(file.id, request);
    try { await request; }
    finally {
      if (lanes.get(file.id) === request) lanes.delete(file.id);
      if (pending.current?.isCurrent === isCurrent) pending.current = null;
    }
  }, [workspaceId, lanes, client, beginSelection, select, notify]);
}
