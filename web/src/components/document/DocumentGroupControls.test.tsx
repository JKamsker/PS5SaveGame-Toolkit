import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, useLocation } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { groupApi, type GroupOffer } from "../../api/groups";
import { api, ApiError } from "../../api/client";
import type { SaveDocument } from "../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { DocumentGroupControls } from "./DocumentGroupControls";
import { deferred } from "../../test/deferred";
import { WorkspaceSelectionContext } from "../../state/workspaceSelection";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const members = [
  { role: "character", fileId: "c", relativePath: "PPSA99990/sdimg_SAVE/character.dat", sha256: "abc", isBackup: false },
  { role: "profile", fileId: "p", relativePath: "PPSA99990/sdimg_SAVE/profile.dat", sha256: "def", isBackup: false },
];
const document: SaveDocument = {
  id: "single", fileId: "c", filePath: members[0]!.relativePath, title: "Character", canEdit: true,
  plugin: { id: "groups", name: "Groups", version: "1.0.0", maturity: "Experimental", games: [], references: [], supportsGroups: true },
  state: { canUndo: false, canRedo: false, isDirty: false, revision: 0 },
  view: { type: "section", title: "Character", children: [] },
};
const offer: GroupOffer = { token: "selected", pluginId: "groups", title: "Character + profile", members, errors: [] };

function Location() { return <output aria-label="Location">{useLocation().search}</output>; }

function setup(doc = document, dirty = false, beginSelection: (() => () => boolean) | null = null) {
  const store = new DocumentStore({ ...doc.state, isDirty: dirty });
  const context: DocumentContextValue = {
    workspaceId: "w1", documentId: doc.id, canEdit: true, store, structureVersion: 0,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true), mutate: async call => call(), acceptResult: vi.fn(async () => {}),
    runAction: vi.fn(async () => ({ ok: true, errors: [], warnings: [], changes: [], structureChanged: false, state: store.state })),
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const view = render(<MemoryRouter initialEntries={["/?doc=single"]}><QueryClientProvider client={client}>
    <WorkspaceSelectionContext.Provider value={beginSelection}><DocumentContext.Provider value={context}><DocumentGroupControls document={doc} /><Location /></DocumentContext.Provider></WorkspaceSelectionContext.Provider>
  </QueryClientProvider></MemoryRouter>);
  return { ...view, client, store };
}

it("ignores a group-open completion after a newer workspace selection", async () => {
  let current = true;
  vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
  const pending = deferred<SaveDocument>();
  const open = vi.spyOn(groupApi, "open").mockReturnValue(pending.promise);
  const { client } = setup(document, false, () => () => current);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Choose related files" })); });
  act(() => { const button = screen.getByRole("button", { name: "Open group" }); fireEvent.click(button); fireEvent.click(button); });
  expect(open).toHaveBeenCalledOnce();
  current = false;
  await act(async () => { pending.resolve({ ...document, id: "group", groupMembers: members }); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("?doc=single");
  expect(client.getQueryData(["document", "w1", "group"])).toBeUndefined();
  client.clear();
});

it("ignores group-close navigation after a newer selection", async () => {
  let current = true;
  const pending = deferred<void>();
  vi.spyOn(api, "closeDocument").mockReturnValue(pending.promise);
  const { client } = setup(document, false, () => () => current);
  fireEvent.click(screen.getByRole("button", { name: "Close document" }));
  current = false;
  await act(async () => { pending.resolve(); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("?doc=single");
  client.clear();
});

it("does not publish a proposal after unmount", async () => {
  const pending = deferred<GroupOffer[]>();
  vi.spyOn(groupApi, "propose").mockReturnValue(pending.promise);
  const { unmount, client } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
  unmount();
  await act(async () => { pending.resolve([offer]); await pending.promise; });
  expect(screen.queryByText("Character + profile")).toBeNull();
  client.clear();
});

it("updates group-offer and close eligibility when the document becomes dirty", async () => {
  vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
  const { client, store } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
  const open = await screen.findByRole<HTMLButtonElement>("button", { name: "Open group" });
  expect(open.disabled).toBe(false);
  act(() => store.apply({ ok: true, errors: [], warnings: [], changes: [], structureChanged: false,
    state: { ...store.state, isDirty: true, revision: 1 } }));
  expect(open.disabled).toBe(true);
  expect(screen.getByRole<HTMLButtonElement>("button", { name: "Close document" }).disabled).toBe(true);
  expect(screen.getByText("Undo this document’s edits before opening a group.").getAttribute("role")).toBe("status");
  client.clear();
});

describe("document groups", () => {
  it("shows explicit partner paths and only opens the selected proposal after confirmation", async () => {
    const propose = vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
    const open = vi.spyOn(groupApi, "open").mockResolvedValue({ ...document, id: "group", groupMembers: members });
    const { client } = setup();
    expect(propose).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
    await screen.findByText(members[1]!.relativePath);
    expect(propose).toHaveBeenCalledWith("w1", "c", "groups");
    expect(open).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Open group" }));
    await waitFor(() => expect(open).toHaveBeenCalledWith("w1", "selected", "single"));
    await waitFor(() => expect(screen.getByLabelText("Location").textContent).toBe("?doc=group"));
    expect(client.getQueryData<SaveDocument>(["document", "w1", "group"])?.groupMembers).toEqual(members);
  });

  it("cancel leaves the single document open and never acquires a group", async () => {
    vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
    const open = vi.spyOn(groupApi, "open");
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("button", { name: "Open group" })).toBeNull();
    expect(open).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Location").textContent).toBe("?doc=single");
  });

  it("shows missing or ambiguous partners and prevents confirmation", async () => {
    vi.spyOn(groupApi, "propose").mockResolvedValue([{ ...offer, errors: [{ valueId: "profile", message: "Matching profile is ambiguous." }] }]);
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
    expect((await screen.findByRole("alert")).textContent).toContain("ambiguous");
    expect((screen.getByRole("button", { name: "Open group" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("retains the proposal and reports a server ownership conflict", async () => {
    vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
    vi.spyOn(groupApi, "open").mockRejectedValue(new ApiError("Profile is open in a dirty session.", 409));
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
    fireEvent.click(await screen.findByRole("button", { name: "Open group" }));
    expect((await screen.findByRole("alert")).textContent).toContain("dirty session");
    expect(screen.getByText(members[1]!.relativePath)).toBeTruthy();
    expect(screen.getByLabelText("Location").textContent).toBe("?doc=single");
  });

  it("prevents replacing a dirty single document", async () => {
    vi.spyOn(groupApi, "propose").mockResolvedValue([offer]);
    setup(document, true);
    fireEvent.click(screen.getByRole("button", { name: "Choose related files" }));
    const button = await screen.findByRole("button", { name: "Open group" });
    expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Undo this document’s edits before opening a group.")).toBeTruthy();
  });

  it("shows all owned members, explicit backup policy and closes a clean group", async () => {
    const close = vi.spyOn(api, "closeDocument").mockResolvedValue();
    setup({ ...document, id: "group", groupMembers: members });
    expect(screen.getByText(members[0]!.relativePath)).toBeTruthy();
    expect(screen.getByText(members[1]!.relativePath)).toBeTruthy();
    expect(screen.getByText(/Only explicitly selected backups/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Choose related files" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Close group" }));
    await waitFor(() => expect(close).toHaveBeenCalledWith("w1", "group"));
    await waitFor(() => expect(screen.getByLabelText("Location").textContent).toBe(""));
  });
});
