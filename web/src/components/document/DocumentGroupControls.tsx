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
  const controller = useDocumentGroup(document);
  return <section className="document-group" aria-label="Document group">
    {document.groupMembers ? <GroupMembers members={document.groupMembers} {...controller} /> : <GroupProposals {...controller} />}
    {controller.error && <p role="alert">{controller.error}</p>}
  </section>;
}

function useDocumentGroup(document: SaveDocument) {
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

  const propose = () => run(async isCurrent => {
    const result = await groupApi.propose(workspaceId, document.fileId, document.plugin.id);
    if (isCurrent()) setOffers(result);
  });
  return { busy, dirty: state.isDirty, offers, error, open, close, propose, cancel: () => setOffers(null) };
}

function GroupMembers({ members, busy, dirty, close }: { members: NonNullable<SaveDocument["groupMembers"]> } & Pick<ReturnType<typeof useDocumentGroup>, "busy" | "dirty" | "close">) {
  return <>
    <h2>Document group</h2>
    <ul>{members.map(member => <li key={member.fileId}>
      <strong>{member.role}</strong>: <span className="mono">{member.relativePath}</span>{member.isBackup && " (backup)"}
    </li>)}</ul>
    <p>Edits, undo and export cover all members together. Only explicitly selected backups are written.</p>
    <button className="btn btn--small" type="button" disabled={busy || dirty}
      onClick={() => void close()}>Close group</button>
    {dirty && <p>Undo the group edits before closing, or close the dump to discard them.</p>}
  </>;
}

function GroupProposals({ busy, dirty, offers, open, close, propose, cancel }: ReturnType<typeof useDocumentGroup>) {
  return <>
    <button className="btn btn--small" type="button" disabled={busy}
      onClick={() => void propose()}>Choose related files</button>
    <button className="btn btn--small btn--quiet" type="button" disabled={busy || dirty}
      onClick={() => void close()}>Close document</button>
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
