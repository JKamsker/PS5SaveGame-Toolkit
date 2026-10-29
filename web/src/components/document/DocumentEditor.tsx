import type { SaveFileInfo } from "../../api/protocol";
import { DocumentContext } from "../../state/documentStore";
import { SourcesPanel } from "./SourcesPanel";
import { ChangesPanel } from "./review/ChangesPanel";
import { ReviewNavigationProvider } from "./review/navigation";
import { ViewRenderer } from "./ViewRenderer";
import { DocumentGroupControls } from "./DocumentGroupControls";
import { DocumentHeader } from "./DocumentHeader";
import { useDocumentSession } from "./useDocumentSession";
import { useEntryFocus } from "../ui/useEntryFocus";

interface DocumentEditorProps {
  workspaceId: string;
  documentId: string;
  file: SaveFileInfo | undefined;
  onSwitchPlugin: (pluginId: string) => void;
  onToast: (message: string, isError?: boolean) => void;
}

export function DocumentEditor(props: DocumentEditorProps) {
  const { workspaceId, documentId, onToast } = props;
  const session = useDocumentSession(workspaceId, documentId, onToast);
  const { query, context, refresh } = session;
  const focusHeading = useEntryFocus(`${workspaceId}:${documentId}:${Boolean(query.data)}`);
  // A cached snapshot may predate edits made before switching files. Wait for the mount refresh
  // before exposing controls, so its response cannot overwrite a newly submitted edit.
  if (query.isPending || (!query.isFetchedAfterMount && query.isFetching)) return <div role="status" className="empty">Opening…</div>;
  if (!context || !query.data) {
    return (
      <div className="empty">
        <h2 ref={focusHeading} tabIndex={-1}>This file could not be opened</h2>
        <p role="alert">{query.error?.message ?? "Unknown error."}</p>
        <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void refresh()}>Retry document</button>
      </div>
    );
  }

  return <OpenDocument {...props} session={session} focusHeading={focusHeading} />;
}

function OpenDocument({ session, focusHeading, workspaceId, documentId, file, onSwitchPlugin, onToast }:
  DocumentEditorProps & { session: ReturnType<typeof useDocumentSession>; focusHeading: ReturnType<typeof useEntryFocus> }) {
  const { query, context, refresh, structureVersion } = session;
  if (!context || !query.data) return null;
  const doc = query.data;
  return (
    <DocumentContext.Provider value={context}>
      <ReviewNavigationProvider>
        <article className="editor">
          <DocumentHeader document={doc} file={file} focusHeading={focusHeading} onSwitchPlugin={onSwitchPlugin} />
          {query.isError && <div className="read-error">
            <p role="alert" className="notice notice--Danger">Could not refresh the document: {query.error.message}</p>
            <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void refresh()}>Refresh document</button>
          </div>}
          {(doc.plugin.supportsGroups || doc.groupMembers) && <DocumentGroupControls document={doc} />}
          <SourcesPanel
            workspaceId={workspaceId}
            documentId={documentId}
            pluginReferences={doc.plugin.references}
            refreshKey={structureVersion}
            onToast={onToast}
          />
          {!doc.canEdit && <p className="notice notice--Muted">This plugin shows the data but cannot write it.</p>}
          <ChangesPanel />
          <ViewRenderer node={doc.view} />
        </article>
      </ReviewNavigationProvider>
    </DocumentContext.Provider>
  );
}
