import { useContext } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Workspace } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";

/** Close a clean session without discarding edits or another document's caches/ownership. */
export function useCloseDocument(onClosed: () => void) {
  const { workspaceId, documentId, store } = useDocument();
  const activity = useContext(WorkspaceActivityContext);
  const client = useQueryClient();
  return async (isCurrent: () => boolean) => {
    // Check live state at activation, including edits queued by a blur immediately before click.
    if (store.state.isDirty) throw new Error("Undo the document’s edits before closing, or export and close the dump.");
    if (activity?.hasPendingMutations) throw new Error("Wait for pending edits to finish, then try closing again.");
    await api.closeDocument(workspaceId, documentId);
    client.setQueryData<Workspace>(["workspace", workspaceId], previous => previous && ({ ...previous,
      documents: previous.documents.filter(document => document.id !== documentId),
      titles: previous.titles.map(title => ({ ...title, directories: title.directories.map(directory => ({ ...directory,
        files: directory.files.map(file => file.openDocumentId === documentId ? { ...file, openDocumentId: undefined } : file),
      })) })),
    }));
    client.removeQueries({ predicate: query => query.queryKey[1] === workspaceId && query.queryKey[2] === documentId });
    void client.invalidateQueries({ queryKey: ["workspace", workspaceId] });
    void client.invalidateQueries({ queryKey: ["export-plan", workspaceId] });
    if (isCurrent()) onClosed();
  };
}
