import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { DocumentSource } from "../../api/protocol";
import { SourcesList, SourcesPanel } from "./SourcesPanel";

const source = (overrides: Partial<DocumentSource>): DocumentSource => ({
  packId: "test.items.en",
  packTitle: "Items Game reference data",
  packVersion: "1.0.0",
  origin: "Embedded",
  locale: "en",
  datasetId: "items",
  kind: "Records",
  title: "Item names",
  itemCount: 3,
  used: true,
  attribution: {
    sourceUrl: "https://example.org/wiki/items",
    author: "Example wiki contributors",
    license: "CC-BY-SA-4.0",
    licenseUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
    retrieved: "2026-10-01",
    modifications: "Extracted id and name columns.",
  },
  ...overrides,
});

const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  const view = (refreshKey: number) => <QueryClientProvider client={client}>
    <SourcesPanel workspaceId="w" documentId="d" pluginReferences={[]} refreshKey={refreshKey} onToast={toast} />
  </QueryClientProvider>;
  const { rerender } = render(view(0));
  const toggle = screen.getByRole("button", { name: /Sources and attribution/ });
  fireEvent.click(toggle);
  return { toast, toggle, refresh: () => rerender(view(1)) };
}

describe("SourcesList", () => {
  it("shows full attribution with licence and source links for datasets in use", () => {
    render(<SourcesList datasets={[source({})]} pluginReferences={[]} />);

    expect(screen.getByText("Item names")).toBeTruthy();
    expect(screen.getByText(/Example wiki contributors/)).toBeTruthy();
    const licence = screen.getByRole("link", { name: "CC-BY-SA-4.0" });
    expect(licence.getAttribute("href")).toBe("https://creativecommons.org/licenses/by-sa/4.0/");
    expect(licence.getAttribute("rel")).toBe("noopener noreferrer");
    expect(screen.getByRole("link", { name: "example.org" }).getAttribute("href")).toBe("https://example.org/wiki/items");
    expect(screen.getByText(/Extracted id and name columns/)).toBeTruthy();
    expect(screen.getByText(/built-in · en · 3 records/)).toBeTruthy();
  });

  it("lists loaded-but-unused datasets separately and explains when nothing is used", () => {
    render(<SourcesList datasets={[source({ used: false, datasetId: "icons", kind: "Assets", title: "Icons" })]} pluginReferences={["https://example.org/tool"]} />);

    expect(screen.getByText(/No reference pack is used here/)).toBeTruthy();
    expect(screen.getByText("Also loaded for this game (1)")).toBeTruthy();
    expect(screen.getByRole("link", { name: "https://example.org/tool" })).toBeTruthy();
  });

  it("never renders non-http URLs as links", () => {
    const evil = source({ attribution: { ...source({}).attribution, licenseUrl: "javascript:alert(1)" } });

    render(<SourcesList datasets={[evil]} pluginReferences={[]} />);

    expect(screen.queryByRole("link", { name: "CC-BY-SA-4.0" })).toBeNull();
    expect(screen.getByText("CC-BY-SA-4.0")).toBeTruthy();
  });
});

describe("SourcesPanel lifecycle", () => {
  it("distinguishes loading from empty results and reloads when the document view changes", async () => {
    let finish!: (value: { datasets: DocumentSource[] }) => void;
    vi.spyOn(api, "sources").mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValue({ datasets: [source({})] });
    const { refresh } = renderPanel();
    expect(screen.getByText("Loading…")).toBeTruthy();
    expect(screen.queryByText(/No reference pack is used here/)).toBeNull();
    await act(async () => { finish({ datasets: [] }); });
    await screen.findByText(/No reference pack is used here/);
    expect(screen.queryByText("Loading…")).toBeNull();
    expect(api.sources).toHaveBeenCalledExactlyOnceWith("w", "d");
    refresh();
    await screen.findByText("Item names");
    expect(screen.getByLabelText("1 datasets in use").textContent).toBe("1");
    expect(screen.queryByText(/No reference pack is used here/)).toBeNull();
    expect(api.sources).toHaveBeenCalledTimes(2);
  });

  it("shows source request failures and recovers by refetching on reopen", async () => {
    vi.spyOn(api, "sources").mockRejectedValueOnce(new ApiError("Sources unavailable", 503))
      .mockResolvedValue({ datasets: [source({})] });
    const { toggle } = renderPanel();
    await screen.findByText("Sources unavailable");
    expect(screen.queryByText(/No reference pack is used here/)).toBeNull();
    fireEvent.click(toggle);
    fireEvent.click(toggle);
    await screen.findByText("Item names");
    expect(screen.queryByText("Sources unavailable")).toBeNull();
    expect(vi.mocked(api.sources).mock.calls).toEqual([["w", "d"], ["w", "d"]]);
  });

  it.each([
    { failure: new ApiError("Invalid pack manifest", 400), message: "Invalid pack manifest" },
    { failure: new Error("Offline"), message: "The pack could not be imported." },
  ])("reports pack import failure: $message, then allows the same file to be retried", async ({ failure, message }) => {
    vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
    const upload = vi.spyOn(api, "importReferencePack").mockRejectedValueOnce(failure).mockResolvedValue({
      id: "test", title: "Fixture pack", version: "1", origin: "User", pluginIds: [], titleIds: [], gameBuilds: [], datasets: [],
    });
    const { toast } = renderPanel();
    await screen.findByText(/No reference pack is used here/);
    const input = screen.getByLabelText("Import reference pack…") as HTMLInputElement;
    const reset = vi.spyOn(input, "value", "set");
    const file = new File(["invented fixture"], "reference.zip", { type: "application/zip" });
    await act(async () => { fireEvent.change(input, { target: { files: [file] } }); });
    expect(toast).toHaveBeenCalledExactlyOnceWith(message, true);
    expect(reset).toHaveBeenCalledExactlyOnceWith("");
    await act(async () => { fireEvent.change(input, { target: { files: [file] } }); });
    expect(upload.mock.calls).toEqual([[file], [file]]);
    expect(toast).toHaveBeenLastCalledWith("Imported “Fixture pack”. Re-open the document to use it.");
    expect(screen.queryByText("Fixture pack")).toBeNull();
    expect(api.sources).toHaveBeenCalledTimes(1);
  });
});
