import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import type { QueryablePage } from "../../../api/protocol";
import { networkMessage } from "../../../api/http";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ServerTableView } from "./ServerTableView";
import { deferred } from "../../../test/deferred";

const clients: QueryClient[] = [];
afterEach(() => {
  cleanup();
  clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const columns = [{ key: "name", label: "Name" }];
const page = (offset: number): QueryablePage => ({ collectionId: "items", revision: 1, columns,
  offset, limit: 50, totalCount: 100, filteredCount: 100, rows: [{ key: offset ? "b" : "a",
    cells: { name: { type: "text", text: offset ? "Ether" : "Potion" } } }] });
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
  status, headers: { "content-type": "application/json" },
});
const failures = [
  { name: "400 JSON", message: "Query invalid; try again", fail: () => Promise.resolve(response({ error: "Query invalid; try again" }, 400)) },
  { name: "404 expired session", message: "Document expired; reopen the save", fail: () => Promise.resolve(response({ error: "Document expired; reopen the save" }, 404)) },
  { name: "503 JSON", message: "Rows unavailable; try again", fail: () => Promise.resolve(response({ error: "Rows unavailable; try again" }, 503)) },
  { name: "502 HTML", message: "Request failed (502). Please try again.", fail: () => Promise.resolve(new Response("<html>Proxy failure</html>", { status: 502, headers: { "content-type": "text/html" } })) },
  { name: "offline", message: networkMessage, fail: () => Promise.reject(new TypeError("Failed to fetch")) },
];

function mount() {
  const store = new DocumentStore({ revision: 1, isDirty: false, canUndo: false, canRedo: false });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", canEdit: true, structureVersion: 0, store,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true), mutate: call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, retryDelay: 0, gcTime: Infinity } } });
  clients.push(client);
  render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", totalCount: 100, columns }} />
  </DocumentContext.Provider></QueryClientProvider>);
  return client;
}

it.each(failures)("retains selection through $name paging failure and retries the requested page", async ({ fail, message }) => {
  let failing = true;
  const fetch = vi.fn().mockImplementation((url: string) => {
    const offset = Number(new URL(url, "http://test").searchParams.get("offset"));
    return offset && failing ? fail() : Promise.resolve(response(page(offset)));
  });
  vi.stubGlobal("fetch", fetch);
  mount();
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Potion" }));
  expect(screen.getByRole("status", { name: "Items selection" }).textContent).toContain("1 selected");
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", message);
  expect(screen.queryByRole("checkbox", { name: "Select Potion" })).toBeNull();
  expect(screen.queryByText("No rows match the current filters.")).toBeNull();
  expect(screen.getByRole("status", { name: "Items selection" }).textContent).toContain("1 selected");
  failing = false;
  fireEvent.click(screen.getByRole("button", { name: "Retry rows" }));
  await screen.findByRole("checkbox", { name: "Select Ether" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("status", { name: "Items selection" }).textContent).toContain("1 selected");
  expect(String(fetch.mock.lastCall?.[0])).toContain("offset=50");
  fireEvent.click(screen.getByRole("button", { name: "Previous" }));
  expect(await screen.findByRole("checkbox", { name: "Select Potion" })).toHaveProperty("checked", true);
});

it.each([true, false])("keeps focus sensible when retry removes its control (focused=%s)", async focused => {
  const fetch = vi.fn().mockResolvedValueOnce(response({ error: "Rows unavailable; try again" }, 400))
    .mockResolvedValueOnce(response(page(0)));
  vi.stubGlobal("fetch", fetch);
  mount();
  const retry = await screen.findByRole("button", { name: "Retry rows" });
  const search = screen.getByRole("searchbox", { name: "Search Items" });
  if (focused) retry.focus();
  else search.focus();
  fireEvent.click(retry);
  await screen.findByRole("checkbox", { name: "Select Potion" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(document.activeElement).toBe(focused ? screen.getByRole("heading", { name: "Items" }) : search);
});

it.each(["Next", "Previous"])("keeps focus at the table heading while %s paging fails", async direction => {
  const pending = deferred<Response>();
  const fetch = vi.fn().mockResolvedValueOnce(response(page(0)));
  if (direction === "Previous") fetch.mockResolvedValueOnce(response(page(50)));
  fetch.mockReturnValueOnce(pending.promise);
  vi.stubGlobal("fetch", fetch);
  mount();
  await screen.findByRole("checkbox", { name: "Select Potion" });
  if (direction === "Previous") {
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await screen.findByRole("checkbox", { name: "Select Ether" });
  }
  const button = screen.getByRole("button", { name: direction });
  button.focus();
  fireEvent.click(button);
  await waitFor(() => expect(button).toHaveProperty("disabled", true));
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Items" }));
  await act(async () => { pending.resolve(response({ error: "Page unavailable; try again" }, 422)); });
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Page unavailable; try again");
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Items" }));
});

it("preserves a loaded page draft and selection while the same page read fails offline", async () => {
  const editable: QueryablePage = { ...page(0), rows: [{ key: "a", cells: { name: {
    type: "field", valueId: "quantity", label: "Quantity", value: "2", spec: { kind: "integer", min: "0", max: "99" },
  } } }] };
  let offline = false;
  vi.stubGlobal("fetch", vi.fn().mockImplementation(() => offline
    ? Promise.reject(new TypeError("Failed to fetch")) : Promise.resolve(response(editable))));
  const client = mount();
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  const checkbox = screen.getByRole("checkbox", { name: "Select 2" });
  fireEvent.click(checkbox);
  fireEvent.change(input, { target: { value: "9" } });
  input.focus();
  offline = true;
  await act(async () => { await client.refetchQueries({ queryKey: ["collection"] }); });
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", networkMessage);
  expect(input.value).toBe("9");
  expect(checkbox).toHaveProperty("checked", true);
  expect(document.activeElement).toBe(input);
  offline = false;
  fireEvent.click(screen.getByRole("button", { name: "Retry rows" }));
  await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  expect(input.value).toBe("9");
  expect(checkbox).toHaveProperty("checked", true);
});
