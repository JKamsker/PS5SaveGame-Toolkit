import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { actionReview } from "../../api/actionReview";
import { api } from "../../api/client";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { HexView } from "./HexView";
import { ChangesPanel } from "./review/ChangesPanel";

vi.mock("@tanstack/react-virtual", () => ({ useVirtualizer: () => ({
  getVirtualItems: () => [{ index: 0, key: 0, start: 0 }], getTotalSize: () => 22, scrollToIndex: vi.fn(),
}) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function setup(children: ReactNode) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", canEdit: true, store,
    structureVersion: 0, refresh: vi.fn(), commit: vi.fn(), mutate: async call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  const rendered = render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    {children}
  </DocumentContext.Provider></QueryClientProvider>);
  return { ...rendered, store, client };
}

describe.each(["hex", "changes"] as const)("%s content cache", kind => {
  async function open() {
    const request = kind === "hex"
      ? vi.spyOn(api, "blob").mockResolvedValue(new Uint8Array([0x41]))
      : vi.spyOn(actionReview, "changes").mockResolvedValue({ changes: [], warnings: [], complete: true, revision: 1 });
    const view = setup(kind === "hex" ? <HexView node={{ type: "hex", blobId: "blob", length: 1 }} /> : <ChangesPanel />);
    if (kind === "changes") {
      const details = view.container.querySelector("details")!;
      details.open = true;
      fireEvent(details, new Event("toggle"));
    }
    await screen.findByText(kind === "hex" ? "41" : "No semantic changes.");
    return { ...view, request };
  }

  it("does not refetch for validation errors or rejected edits", async () => {
    const { request, store, client } = await open();
    for (let i = 0; i < 10; i++) {
      await act(async () => store.setError("quantity", `Rejected ${i}`));
      await act(async () => store.apply({ ok: false, errors: [{ valueId: "quantity", message: "Rejected" }],
        changes: [], warnings: [], structureChanged: false, state: store.state }));
    }
    expect(request).toHaveBeenCalledTimes(1);
    client.clear();
  });

  it("refetches for scalar changes, structural revisions and resets", async () => {
    const { request, store, client } = await open();
    act(() => store.apply({ ok: true, errors: [], warnings: [], structureChanged: false, state: store.state,
      changes: [{ valueId: "quantity", oldValue: "1", newValue: "2" }] }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    act(() => store.apply({ ok: true, errors: [], warnings: [], structureChanged: false, changes: [],
      state: { ...store.state, revision: 2 } }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    act(() => store.reset(store.state));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(4));
    client.clear();
  });
});
