import { StrictMode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SectionNode, ViewNode } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ViewList } from "./ViewRenderer";

const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });

const lazy = (id: string, collapsed = false): SectionNode => ({
  type: "section", title: id, lazyId: id, collapsed, children: [],
});
const field = (id: string): ViewNode => ({
  type: "field", valueId: id, label: id, spec: { kind: "bool" }, value: false,
});

function mount(nodes: ViewNode[], strict = false, cachedNodes: Record<string, ViewNode[]> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  for (const [id, children] of Object.entries(cachedNodes)) client.setQueryData(["nodes", "w", "fake", id, 1], children);
  const context = { workspaceId: "w", documentId: "fake", canEdit: true, structureVersion: 1,
    store: new DocumentStore({ canUndo: false, canRedo: false, isDirty: false }),
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => false),
    acceptResult: vi.fn(async () => {}), runAction: vi.fn() };
  const view = (version: number) => <QueryClientProvider client={client}>
    <DocumentContext.Provider value={{ ...context, structureVersion: version }}>
      <ViewList nodes={nodes} />
    </DocumentContext.Provider>
  </QueryClientProvider>;
  const result = render(strict ? <StrictMode>{view(1)}</StrictMode> : view(1));
  return { ...result, client, refresh: () => result.rerender(view(2)) };
}

describe("lazy expansion safeguards", () => {
  it("bounds a deep default-open chain and loads only the requested next level", async () => {
    const expand = vi.spyOn(api, "expand").mockImplementation(async (_w, _d, id) => {
      const depth = Number(id);
      return [field(`Field ${depth}`), ...(depth < 1000 ? [lazy(String(depth + 1))] : [])];
    });
    const { container } = mount([lazy("0")]);
    await screen.findByRole("switch", { name: "Field 0" });
    expect(expand).toHaveBeenCalledTimes(1);
    expect(container.querySelectorAll("details").length).toBe(2);
    expect(screen.getAllByRole("switch")).toHaveLength(1);
    expect(screen.getByText("1").closest("details")!.open).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Load 1" }));
    await screen.findByRole("switch", { name: "Field 1" });
    expect(document.activeElement).toBe(screen.getByText("1").closest("summary"));
    expect(expand).toHaveBeenCalledTimes(2);
    expect(screen.getAllByRole("switch")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Load 2" })).toBeTruthy();
    expect(container.querySelectorAll("details").length).toBe(3);
  });

  it("caps broad automatic loading across eager wrappers and tabs, including StrictMode", async () => {
    const expand = vi.spyOn(api, "expand").mockImplementation(async (_w, _d, id) => [field(id)]);
    const roots = Array.from({ length: 30 }, (_, i) => lazy(`Root ${i}`));
    const { container } = mount([{ type: "tabs", tabs: [{ title: "Systems", children: [
      { type: "section", title: "Eager wrapper", children: roots },
    ] }, { title: "Other", children: [lazy("Other root")] }] }], true);
    await waitFor(() => expect(screen.getAllByRole("switch")).toHaveLength(8));
    expect(expand).toHaveBeenCalledTimes(8);
    expect(container.querySelectorAll("details[open]").length).toBe(9);
    expect(screen.getAllByRole("button", { name: /^Load Root/ })).toHaveLength(22);
    fireEvent.click(screen.getByRole("button", { name: "Load Root 29" }));
    await screen.findByRole("switch", { name: "Root 29" });
    expect(expand).toHaveBeenCalledTimes(9);
    fireEvent.click(screen.getByRole("tab", { name: "Other" }));
    expect(screen.getByRole("button", { name: "Load Other root" })).toBeTruthy();
    expect(expand).toHaveBeenCalledTimes(9);
    fireEvent.click(screen.getByRole("tab", { name: "Systems" }));
    await waitFor(() => expect(screen.getAllByRole("switch")).toHaveLength(8));
    expect(expand).toHaveBeenCalledTimes(9);
  });

  it("keeps ordinary eager and top-level lazy sections open and collapsed sections on demand", async () => {
    const expand = vi.spyOn(api, "expand").mockImplementation(async (_w, _d, id) => [field(id)]);
    mount([{ type: "section", title: "Normal", children: [field("Eager"), lazy("Automatic"), lazy("Manual", true)] }]);
    await screen.findByRole("switch", { name: "Automatic" });
    expect(screen.getByRole("switch", { name: "Eager" })).toBeTruthy();
    expect(expand).toHaveBeenCalledTimes(1);
    const details = screen.getByText("Manual").closest("details")!;
    details.open = true;
    fireEvent(details, new Event("toggle"));
    await screen.findByRole("switch", { name: "Manual" });
    expect(expand).toHaveBeenCalledTimes(2);
  });

  it("caps both automatic and explicit concurrent requests and cancels obsolete queued work", async () => {
    let active = 0;
    let peak = 0;
    const pending: (() => void)[] = [];
    const expand = vi.spyOn(api, "expand").mockImplementation((_w, _d, id) => new Promise(resolve => {
      active++;
      peak = Math.max(peak, active);
      pending.push(() => { active--; resolve([field(id)]); });
    }));
    const { unmount } = mount(Array.from({ length: 12 }, (_, i) => lazy(`Queued ${i}`)));
    await waitFor(() => expect(expand).toHaveBeenCalledTimes(4));
    fireEvent.click(screen.getByRole("button", { name: "Load Queued 11" }));
    expect(expand).toHaveBeenCalledTimes(4);
    await act(async () => { pending.shift()!(); });
    await waitFor(() => expect(expand).toHaveBeenCalledTimes(5));
    expect(peak).toBe(4);
    unmount();
    await act(async () => { pending.splice(0).forEach(finish => finish()); });
    expect(expand).toHaveBeenCalledTimes(5);
  });

  it("refreshes a new document generation without recursively opening its descendants", async () => {
    const expand = vi.spyOn(api, "expand").mockImplementation(async (_w, _d, id) => [field(id), lazy(`${id}/child`)]);
    const { refresh } = mount([lazy("Root")]);
    await screen.findByRole("switch", { name: "Root" });
    refresh();
    await waitFor(() => expect(expand).toHaveBeenCalledTimes(2));
    await screen.findByRole("button", { name: "Load Root/child" });
    expect(screen.getAllByRole("switch")).toHaveLength(1);
  });

  it("bounds rendering even when a deep lazy hierarchy is already cached", async () => {
    const expand = vi.spyOn(api, "expand");
    const cached = Object.fromEntries(Array.from({ length: 1000 }, (_, i) => [String(i), [field(`Cached ${i}`), lazy(String(i + 1))]]));
    const { container } = mount([lazy("0")], false, cached);
    await screen.findByRole("switch", { name: "Cached 0" });
    expect(screen.getAllByRole("switch")).toHaveLength(1);
    expect(container.querySelectorAll("details").length).toBe(2);
    fireEvent.click(screen.getByRole("button", { name: "Load 1" }));
    await screen.findByRole("switch", { name: "Cached 1" });
    expect(screen.getAllByRole("switch")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Load 2" })).toBeTruthy();
    expect(expand).not.toHaveBeenCalled();
  });
});
