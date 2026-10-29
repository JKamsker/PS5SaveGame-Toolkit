import { useContext, useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { groupApi, type GroupOffer } from "../../api/groups";
import type { SaveDocument } from "../../api/protocol";
import { useDocument, useDocumentState } from "../../state/documentStore";
import { WorkspaceSelectionContext } from "../../state/workspaceSelection";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";
import { useCloseDocument } from "./useCloseDocument";
import { documentClosures, useDocumentCloseState } from "./documentClosures";
import "./document-groups.css";

interface DocumentControlsProps {
  document: SaveDocument;
  onClosed?: () => void;
  onOpenGroup?: (documentId: string) => void;
}
const noop = () => {};

export function DocumentGroupControls({ document, onClosed = noop, onOpenGroup = noop }: DocumentControlsProps) {
  const controller = useDocumentGroup(document, onClosed, onOpenGroup);
  const controls = useRef<HTMLElement>(null);
  useEffect(() => {
    const active = window.document.activeElement;
    if (controller.error && (active === window.document.body || (controller.closeError && active?.classList.contains("editor__title")))) {
      controls.current?.querySelector<HTMLButtonElement>("[data-close-document]")?.focus();
    }
  }, [controller.error, controller.closeError]);
  return <section ref={controls} className="document-group" aria-label={document.plugin.supportsGroups || document.groupMembers ? "Document group" : "Document controls"}>
    {document.groupMembers ? <GroupMembers members={document.groupMembers} {...controller} /> : <>
      {document.plugin.supportsGroups && <GroupProposals {...controller} />}
      <button data-close-document className="btn btn--small btn--quiet" type="button" disabled={controller.busy || controller.dirty}
        aria-describedby="document-close-help" onClick={() => void controller.close()}>Close document</button>
      <p id="document-close-help">{controller.dirty ? "Undo this document’s edits before closing, or export and close the dump." :
        "Close this document, then select its file again to reopen it with newly imported reference packs."}</p>
    </>}
    {controller.error && <p role="alert">{controller.error}</p>}
  </section>;
}

function useDocumentGroup(document: SaveDocument, onClosed: () => void, onOpenGroup: (documentId: string) => void) {
  const { workspaceId, store } = useDocument();
  const { state } = useDocumentState(store);
  const queryClient = useQueryClient();
  const closeDocument = useCloseDocument(onClosed);
  const closeState = useDocumentCloseState(workspaceId, document.id);
  const [offers, setOffers] = useState<GroupOffer[] | null>(null);
  const { busy, run: submit } = useAsyncTask(document.id);
  const fallbackSelection = useLatestRequest(`${workspaceId}:${document.id}`);
  const beginSelection = useContext(WorkspaceSelectionContext) ?? fallbackSelection;
  const [error, setError] = useState<string | null>(null);

  const run = (action: (isCurrent: () => boolean) => Promise<void>, failureMessage = "Could not open the document group.") => submit(async isCurrent => {
    const selectionIsCurrent = beginSelection();
    const current = () => isCurrent() && selectionIsCurrent();
    setError(null);
    try { await action(current); }
    catch (failure) { if (current()) setError(failure instanceof Error ? failure.message : failureMessage); }
  });

  const open = (offer: GroupOffer) => run(async isCurrent => {
    if (documentClosures(queryClient).get(workspaceId, document.id).pending) return;
    const group = await groupApi.open(workspaceId, offer.token, document.id);
    void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
    if (!isCurrent()) return;
    queryClient.removeQueries({ queryKey: ["document", workspaceId, document.id] });
    queryClient.setQueryData(["document", workspaceId, group.id], group);
    onOpenGroup(group.id);
  });

  const close = () => {
    if (busy || documentClosures(queryClient).get(workspaceId, document.id).pending) return;
    return submit(async () => {
      setError(null);
      // Selection ownership belongs to the workspace, not the editor that closing unmounts.
      await closeDocument(beginSelection());
    });
  };

  const propose = () => run(async isCurrent => {
    const result = await groupApi.propose(workspaceId, document.fileId, document.plugin.id);
    if (isCurrent()) setOffers(result);
  });
  return { busy: busy || closeState.pending, dirty: state.isDirty, offers, error: error ?? closeState.error,
    closeError: closeState.error, open, close, propose, cancel: () => setOffers(null) };
}

function GroupMembers({ members, busy, dirty, close }: { members: NonNullable<SaveDocument["groupMembers"]> } & Pick<ReturnType<typeof useDocumentGroup>, "busy" | "dirty" | "close">) {
  return <>
    <h2>Document group</h2>
    <ul>{members.map(member => <li key={member.fileId}>
      <strong>{member.role}</strong>: <span className="mono">{member.relativePath}</span>{member.isBackup && " (backup)"}
    </li>)}</ul>
    <p>Edits, undo and export cover all members together. Only explicitly selected backups are written.</p>
    <button data-close-document className="btn btn--small" type="button" disabled={busy || dirty}
      onClick={() => void close()}>Close group</button>
    {dirty && <p>Undo the group edits before closing, or close the dump to discard them.</p>}
  </>;
}

function GroupProposals({ busy, dirty, offers, open, propose, cancel }: ReturnType<typeof useDocumentGroup>) {
  return <>
    <button className="btn btn--small" type="button" disabled={busy}
      onClick={() => void propose()}>Choose related files</button>
    {offers && <>
      <h2>Open related files together</h2>
      <p>Confirm the exact members below. Opening replaces this clean document with one group and one undo history. Close other open member documents first.</p>
      {dirty && <p role="status">Undo this document’s edits before opening a group.</p>}
      {offers.length === 0 && <p>No supported partners were found.</p>}
      {offers.map(offer => <div key={offer.token}>
        <h3>{offer.title}</h3>
        <ul>{offer.members.map(member => <li key={member.role}>
          <strong>{member.role}</strong>: <span className="mono">{member.relativePath}</span>{member.isBackup && " (backup)"}
        </li>)}</ul>
        {offer.errors.map((problem, index) => <p role="alert" key={index}>{problem.message}</p>)}
        <button className="btn btn--small" type="button" disabled={busy || dirty || offer.errors.length > 0}
          onClick={() => void open(offer)}>Open group</button>
      </div>)}
      <button className="btn btn--small btn--quiet" type="button" disabled={busy} onClick={cancel}>Cancel</button>
    </>}
  </>;
}
