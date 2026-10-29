import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { HexNode } from "../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { HexView } from "./HexView";

vi.mock("@tanstack/react-virtual", () => ({ useVirtualizer: () => ({
  getVirtualItems: () => [{ index: 0, key: 0, start: 0 }], getTotalSize: () => 22, scrollToIndex: vi.fn(),
}) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function setup() {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 0 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", canEdit: true, store, structureVersion: 0,
    refresh: vi.fn(), commit: vi.fn(), mutate: async call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  const view = (node: HexNode) => <QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <HexView node={node} />
  </DocumentContext.Provider></QueryClientProvider>;
  const node: HexNode = { type: "hex", blobId: "first", length: 1 };
  const rendered = render(view(node));
  return { store, client, rerender: (next = node) => rendered.rerender(view(next)) };
}

describe("hex page lifecycle", () => {
  it("reloads bytes after edit, undo and redo", async () => {
    vi.spyOn(api, "blob").mockResolvedValueOnce(new Uint8Array([0x41]))
      .mockResolvedValueOnce(new Uint8Array([0x42])).mockResolvedValueOnce(new Uint8Array([0x41]))
      .mockResolvedValueOnce(new Uint8Array([0x42]));
    const { store, client } = setup();
    await screen.findByText("41");
    for (const [revision, hex] of [[1, "42"], [2, "41"], [3, "42"]] as const) {
      act(() => store.apply({ ok: true, errors: [], warnings: [], structureChanged: false, changes: [],
        state: { canUndo: true, canRedo: true, isDirty: true, revision } }));
      await screen.findByText(hex);
    }
    expect(api.blob).toHaveBeenCalledTimes(4);
    expect(screen.getByRole("region", { name: "Bytes hex dump" }).tabIndex).toBe(0);
    client.clear();
  });

  it("ignores a late response belonging to the previous blob", async () => {
    let finish!: (bytes: Awaited<ReturnType<typeof api.blob>>) => void;
    vi.spyOn(api, "blob").mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(new Uint8Array([0x42]));
    const { rerender, client } = setup();
    await waitFor(() => expect(api.blob).toHaveBeenCalledTimes(1));
    rerender({ type: "hex", blobId: "second", length: 1 });
    await screen.findByText("42");
    await act(async () => { finish(new Uint8Array([0x41])); });
    expect(screen.queryByText("41")).toBeNull();
    expect(screen.getByText("42")).toBeTruthy();
    client.clear();
  });

  it("shows a failed read and retries it", async () => {
    vi.spyOn(api, "blob").mockRejectedValueOnce(new ApiError("Offline", 503)).mockResolvedValueOnce(new Uint8Array([0x41]));
    const { client } = setup();
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("Offline"));
    fireEvent.click(screen.getByRole("button", { name: "Retry bytes" }));
    await screen.findByText("41");
    expect(screen.queryByRole("alert")).toBeNull();
    client.clear();
  });

  it("does not request bytes for an empty blob", () => {
    vi.spyOn(api, "blob").mockImplementation(() => new Promise(() => {}));
    const { rerender, client } = setup();
    rerender({ type: "hex", blobId: "empty", length: 0 });
    expect(api.blob).not.toHaveBeenCalledWith("w", "d", "empty", expect.anything(), expect.anything());
    client.clear();
  });
});
