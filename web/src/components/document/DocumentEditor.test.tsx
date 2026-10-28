import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { DocumentEditor } from "./DocumentEditor";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function setup() {
  let value = false;
  let revision = 0;
  const snapshot = (): SaveDocument => ({ id: "d", fileId: "f", title: "Group save", filePath: "save.dat", canEdit: true,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { canUndo: value, canRedo: !value && revision > 0, isDirty: value, revision },
    view: { type: "section", title: "Members", children: [], lazyId: "members" } });
  const mutation = (next: boolean): EditResult => {
    const previous = value;
    value = next;
    revision++;
    return { ok: true, changes: [{ valueId: "profile/flag", oldValue: previous, newValue: value }],
      errors: [], warnings: [], structureChanged: false, state: snapshot().state };
  };
  vi.spyOn(api, "document").mockImplementation(async () => snapshot());
  vi.spyOn(api, "expand").mockImplementation(async () => [{ type: "field", valueId: "profile/flag", label: "Profile flag", spec: { kind: "bool" }, value }]);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  vi.spyOn(api, "edit").mockImplementation(async () => mutation(!value));
  vi.spyOn(api, "undo").mockImplementation(async () => mutation(false));
  vi.spyOn(api, "redo").mockImplementation(async () => mutation(true));
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  client.setQueryData(["document", "w", "d"], snapshot());
  const toast = vi.fn();
  const mount = () => render(<QueryClientProvider client={client}>
    <DocumentEditor workspaceId="w" documentId="d" file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} />
  </QueryClientProvider>);
  return { client, toast, mutation, mount, ...mount() };
}

describe("document snapshot lifecycle", () => {
  it("keeps expanded group fields current after edits, undo/redo and background refresh", async () => {
    const { client } = setup();
    const control = await screen.findByRole("switch", { name: "Profile flag" });
    fireEvent.click(control);
    await waitFor(() => expect(control.getAttribute("aria-checked")).toBe("true"));
    await act(async () => { await client.refetchQueries({ queryKey: ["document", "w", "d"] }); });
    await waitFor(() => expect(screen.getByRole("switch", { name: /Profile flag/ }).getAttribute("aria-checked")).toBe("true"));
    fireEvent.click(screen.getByRole("button", { name: "Undo" }));
    await waitFor(() => expect(screen.getByRole("switch", { name: /Profile flag/ }).getAttribute("aria-checked")).toBe("false"));
    fireEvent.click(screen.getByRole("button", { name: "Redo" }));
    await waitFor(() => expect(screen.getByRole("switch", { name: /Profile flag/ }).getAttribute("aria-checked")).toBe("true"));
    expect(api.expand).toHaveBeenCalledTimes(2);
    client.clear();
  });

  it("refreshes cached document state when returning to a recently edited file", async () => {
    const { mount, unmount, client, mutation } = setup();
    await screen.findByRole("switch");
    mutation(true);
    unmount();
    mount();
    await waitFor(() => expect(screen.getByRole("switch").getAttribute("aria-checked")).toBe("true"));
    expect((screen.getByRole("button", { name: "Undo" }) as HTMLButtonElement).disabled).toBe(false);
    client.clear();
  });

  it("keeps the editor available and exposes a failed stale refresh with retry", async () => {
    const { client } = setup();
    await screen.findByRole("switch");
    vi.mocked(api.edit).mockResolvedValue({ ok: false, stale: true, errors: [], warnings: [], changes: [], structureChanged: false,
      state: { canUndo: false, canRedo: false, isDirty: false, revision: 1 } });
    vi.mocked(api.document).mockRejectedValueOnce(new ApiError("Refresh unavailable", 503));
    fireEvent.click(screen.getByRole("switch"));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("Refresh unavailable"));
    expect(screen.getByRole("heading", { name: "Group save" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Refresh document" }));
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    client.clear();
  });

  it("does not let an older in-flight snapshot overwrite a completed edit", async () => {
    const { client } = setup();
    await screen.findByRole("switch");
    const old = client.getQueryData<SaveDocument>(["document", "w", "d"])!;
    let finish!: (value: SaveDocument) => void;
    vi.mocked(api.document).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const refresh = client.refetchQueries({ queryKey: ["document", "w", "d"] });
    await waitFor(() => expect(api.document).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByRole("switch"));
    await waitFor(() => expect((screen.getByRole("button", { name: "Undo" }) as HTMLButtonElement).disabled).toBe(false));
    await act(async () => { finish(old); await refresh; });
    expect((screen.getByRole("button", { name: "Undo" }) as HTMLButtonElement).disabled).toBe(false);
    expect(screen.getByRole("switch").getAttribute("aria-checked")).toBe("true");
    client.clear();
  });

  it("bounds stale lazy-node recovery across successive document generations", async () => {
    const { client } = setup();
    await screen.findByRole("switch");
    vi.mocked(api.expand).mockRejectedValue(new ApiError("Stale node", 409, { code: "stale" }));
    await act(async () => { await client.refetchQueries({ queryKey: ["document", "w", "d"] }); });
    await waitFor(() => expect(api.document).toHaveBeenCalledTimes(3));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("section is out of date"));
    fireEvent.click(screen.getByRole("button", { name: "Refresh document" }));
    await waitFor(() => expect(api.document).toHaveBeenCalledTimes(4));
    await screen.findByRole("alert");
    expect(api.document).toHaveBeenCalledTimes(4);
    client.clear();
  });
});
