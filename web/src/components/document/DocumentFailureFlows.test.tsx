import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { networkMessage } from "../../api/http";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { deferred } from "../../test/deferred";
import { DocumentEditor } from "./DocumentEditor";

const clients: QueryClient[] = [];
afterEach(() => {
  cleanup();
  clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const state = { revision: 4, isDirty: true, canUndo: true, canRedo: true };
const snapshot: SaveDocument = { id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] }, state,
  view: { type: "field", valueId: "quantity", label: "Quantity", value: "2", spec: { kind: "integer", min: "0", max: "99" } } };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
  status, headers: { "content-type": "application/json" },
});
const accepted = (value: string): EditResult => ({ ok: true, errors: [], warnings: [], structureChanged: false,
  changes: [{ valueId: "quantity", oldValue: "2", newValue: value }], state: { ...state, revision: 5 } });
const failures = [
  { name: "400 JSON", message: "Invalid request; try again", fail: () => Promise.resolve(response({ error: "Invalid request; try again" }, 400)) },
  { name: "404 expiry", message: "Document expired; reopen the save", fail: () => Promise.resolve(response({ error: "Document expired; reopen the save" }, 404)) },
  { name: "503 problem JSON", message: "Unavailable: Try again shortly", fail: () => Promise.resolve(response({ title: "Unavailable", detail: "Try again shortly" }, 503)) },
  { name: "502 HTML", message: "Request failed (502). Please try again.", fail: () => Promise.resolve(new Response("<html>Proxy failure</html>", { status: 502, headers: { "content-type": "text/html" } })) },
  { name: "offline", message: networkMessage, fail: () => Promise.reject(new TypeError("Failed to fetch")) },
];

async function mount() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  const view = render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>);
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: "Quantity" });
  return { ...view, toast, input };
}

it.each(failures)("preserves edit drafts and history after $name, then retries once", async ({ fail, message }) => {
  const fetch = vi.fn().mockImplementationOnce(fail).mockResolvedValueOnce(response(accepted("9")));
  vi.stubGlobal("fetch", fetch);
  const { toast, input } = await mount();
  input.focus();
  fireEvent.change(input, { target: { value: "9" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(input.getAttribute("aria-invalid")).toBe("true"));
  expect(toast).toHaveBeenCalledWith(message, true);
  expect(input.value).toBe("9");
  expect(document.activeElement).toBe(input);
  expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", false);
  expect(screen.getByRole("button", { name: "Redo" })).toHaveProperty("disabled", false);
  expect(fetch).toHaveBeenCalledOnce();
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(input.getAttribute("aria-invalid")).toBe("false"));
  expect(fetch).toHaveBeenCalledTimes(2);
  expect(fetch.mock.calls.map(call => JSON.parse((call[1] as RequestInit).body as string))).toEqual([
    { edits: [{ valueId: "quantity", value: "9" }], revision: 4 },
    { edits: [{ valueId: "quantity", value: "9" }], revision: 4 },
  ]);
  expect(input.value).toBe("9");
});

describe.each(["undo", "redo"] as const)("%s failures", kind => {
  it.each(["click", "shortcut"])("keeps focus at the document heading while disabling a focused history control (%s)", async initiation => {
    const pending = deferred<Response>();
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(pending.promise));
    const { toast } = await mount();
    const button = screen.getByRole("button", { name: kind === "undo" ? "Undo" : "Redo" });
    button.focus();
    if (initiation === "click") fireEvent.click(button);
    else fireEvent.keyDown(window, { key: "z", ctrlKey: true, shiftKey: kind === "redo" });
    await waitFor(() => expect(button).toHaveProperty("disabled", true));
    expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Save" }));
    await act(async () => { pending.resolve(response({ error: "History unavailable; try again" }, 422)); });
    await waitFor(() => expect(toast).toHaveBeenCalledWith("History unavailable; try again", true));
    expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Save" }));
  });

  it("does not interrupt focus outside the history toolbar during a programmatic request", async () => {
    const pending = deferred<Response>();
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(pending.promise));
    const { toast, input } = await mount();
    input.focus();
    fireEvent.click(screen.getByRole("button", { name: kind === "undo" ? "Undo" : "Redo" }));
    expect(document.activeElement).toBe(input);
    await act(async () => { pending.resolve(response({ error: "History unavailable; try again" }, 422)); });
    await waitFor(() => expect(toast).toHaveBeenCalledWith("History unavailable; try again", true));
    expect(document.activeElement).toBe(input);
  });

  it.each(failures)("retains local state through $name and explicit retry", async ({ fail, message }) => {
    const fetch = vi.fn().mockImplementationOnce(fail).mockResolvedValueOnce(response(accepted("3")));
    vi.stubGlobal("fetch", fetch);
    const { toast, input } = await mount();
    fireEvent.change(input, { target: { value: "9" } });
    const button = screen.getByRole("button", { name: kind === "undo" ? "Undo" : "Redo" });
    button.focus();
    fireEvent.click(button);
    await waitFor(() => expect(toast).toHaveBeenCalledWith(message, true));
    await waitFor(() => expect(button).toHaveProperty("disabled", false));
    expect(input.value).toBe("9");
    expect(screen.queryByText("modified")).toBeNull();
    expect(fetch).toHaveBeenCalledOnce();
    fireEvent.click(button);
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(button).toHaveProperty("disabled", false));
    expect(input.value).toBe("9");
    expect(fetch.mock.calls.every(call => String(call[0]).endsWith(`/${kind}`))).toBe(true);
  });
});

it.each(["resolve", "reject"] as const)("ignores a late edit %s after leaving the document", async outcome => {
  const pending = deferred<Response>();
  const fetch = vi.fn().mockReturnValue(pending.promise);
  vi.stubGlobal("fetch", fetch);
  const { toast, input, unmount } = await mount();
  fireEvent.change(input, { target: { value: "9" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
  unmount();
  await act(async () => {
    if (outcome === "resolve") pending.resolve(response(accepted("9")));
    else pending.reject(new DOMException("Aborted", "AbortError"));
    await pending.promise.catch(() => {});
  });
  expect(toast).not.toHaveBeenCalled();
});
