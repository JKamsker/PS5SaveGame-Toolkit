import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { api } from "../../api/client";
import { groupApi, type GroupOffer } from "../../api/groups";
import type { SaveDocument } from "../../api/protocol";
import { useDocument, useDocumentState } from "../../state/documentStore";
import { useWorkspaceSelection } from "../../state/workspaceSelection";
import { useAsyncTask } from "../ui/useAsyncTask";
import "./document-groups.css";

export function DocumentGroupControls({ document }: { document: SaveDocument }) {
  const { workspaceId, store } = useDocument();
  const { state } = useDocumentState(store);
  const queryClient = useQueryClient();
  const [, setSearch] = useSearchParams();
  const [offers, setOffers] = useState<GroupOffer[] | null>(null);
  const { busy, run: submit } = useAsyncTask(document.id);
  const beginSelection = useWorkspaceSelection();
  const [error, setError] = useState<string | null>(null);

  const run = (action: (isCurrent: () => boolean) => Promise<void>) => submit(async isCurrent => {
    const selectionIsCurrent = beginSelection();
    const current = () => isCurrent() && selectionIsCurrent();
    setError(null);
    try { await action(current); }
    catch (failure) { if (current()) setError(failure instanceof Error ? failure.message : "Could not open the document group."); }
  });

  const open = (offer: GroupOffer) => run(async isCurrent => {
    const group = await groupApi.open(workspaceId, offer.token, document.id);
    void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
    if (!isCurrent()) return;
    queryClient.removeQueries({ queryKey: ["document", workspaceId, document.id] });
    queryClient.setQueryData(["document", workspaceId, group.id], group);
    setSearch({ doc: group.id });
  });

  const close = () => run(async isCurrent => {
    await api.closeDocument(workspaceId, document.id);
    void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
    if (!isCurrent()) return;
    setSearch({});
  });

  return <section className="document-group" aria-label="Document group">
    {document.groupMembers ? <>
      <h2>Document group</h2>
      <ul>{document.groupMembers.map(member => <li key={member.fileId}>
        <strong>{member.role}</strong>: <span className="mono">{member.relativePath}</span>{member.isBackup && " (backup)"}
      </li>)}</ul>
      <p>Edits, undo and export cover all members together. Only explicitly selected backups are written.</p>
      <button className="btn btn--small" type="button" disabled={busy || state.isDirty}
        onClick={() => void close()}>Close group</button>
      {state.isDirty && <p>Undo the group edits before closing, or close the dump to discard them.</p>}
    </> : <>
      <button className="btn btn--small" type="button" disabled={busy}
        onClick={() => void run(async isCurrent => {
          const result = await groupApi.propose(workspaceId, document.fileId, document.plugin.id);
          if (isCurrent()) setOffers(result);
        })}>
        Choose related files
      </button>
      <button className="btn btn--small btn--quiet" type="button" disabled={busy || state.isDirty}
        onClick={() => void close()}>Close document</button>
      {offers && <>
        <h2>Open related files together</h2>
        <p>Confirm the exact members below. Opening replaces this clean document with one group and one undo history. Close other open member documents first.</p>
        {state.isDirty && <p role="status">Undo this document’s edits before opening a group.</p>}
        {offers.length === 0 && <p>No supported partners were found.</p>}
        {offers.map(offer => <div key={offer.token}>
          <h3>{offer.title}</h3>
          <ul>{offer.members.map(member => <li key={member.role}>
            <strong>{member.role}</strong>: <span className="mono">{member.relativePath}</span>{member.isBackup && " (backup)"}
          </li>)}</ul>
          {offer.errors.map((problem, index) => <p role="alert" key={index}>{problem.message}</p>)}
          <button className="btn btn--small" type="button" disabled={busy || state.isDirty || offer.errors.length > 0}
            onClick={() => void open(offer)}>Open group</button>
        </div>)}
        <button className="btn btn--small btn--quiet" type="button" disabled={busy} onClick={() => setOffers(null)}>Cancel</button>
      </>}
    </>}
    {error && <p role="alert">{error}</p>}
  </section>;
}
