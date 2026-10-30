import { QueryClient } from "@tanstack/react-query";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SaveDocument } from "../../api/protocol";
import { deferred } from "../../test/deferred";
import { readDocumentSnapshot } from "./documentSnapshot";

const key = ["document", "workspace", "document"];
const clients: QueryClient[] = [];
afterEach(() => { clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });

function snapshot(revision: number): SaveDocument {
  return { id: "document", fileId: "file", title: "Save", filePath: "save.dat", canEdit: true,
    state: { revision, isDirty: false, canUndo: false, canRedo: false },
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    view: { type: "text", text: "Current document" } };
}

function cachedClient() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(key, snapshot(1));
  return client;
}

it("requires a fresh API read and installs its revision despite cached success", async () => {
  const client = cachedClient();
  const count = client.getQueryState(key)!.dataUpdateCount;
  const read = vi.spyOn(api, "document").mockResolvedValue(snapshot(2));
  expect(await readDocumentSnapshot(client, "workspace", "document", () => true)).toBeUndefined();
  expect(read).toHaveBeenCalledExactlyOnceWith("workspace", "document");
  expect(client.getQueryData<SaveDocument>(key)!.state.revision).toBe(2);
  expect(client.getQueryState(key)!.dataUpdateCount).toBeGreaterThan(count);
});

it("keeps cancelled cached success unproven and prevents the late response from installing", async () => {
  const client = cachedClient();
  const pending = deferred<SaveDocument>();
  const read = vi.spyOn(api, "document").mockReturnValue(pending.promise);
  const result = readDocumentSnapshot(client, "workspace", "document", () => true);
  await vi.waitFor(() => expect(read).toHaveBeenCalledOnce());
  await client.cancelQueries({ queryKey: key });
  expect(await result).toBeTruthy();
  pending.resolve(snapshot(2));
  await pending.promise;
  expect(client.getQueryData<SaveDocument>(key)!.state.revision).toBe(1);
});

it("does not publish an old lifetime over a newly reopened same-id document", async () => {
  const client = cachedClient();
  const oldRead = deferred<SaveDocument>();
  const read = vi.spyOn(api, "document").mockReturnValueOnce(oldRead.promise).mockResolvedValue(snapshot(3));
  let active = true;
  const oldResult = readDocumentSnapshot(client, "workspace", "document", () => active);
  await vi.waitFor(() => expect(read).toHaveBeenCalledOnce());
  active = false;
  expect(await readDocumentSnapshot(client, "workspace", "document", () => true)).toBeUndefined();
  oldRead.resolve(snapshot(2));
  await oldRead.promise;
  expect(await oldResult).toBeTruthy();
  expect(client.getQueryData<SaveDocument>(key)!.state.revision).toBe(3);
});

it("retains the last snapshot on read failure and permits a fresh successful retry", async () => {
  const client = cachedClient();
  vi.spyOn(api, "document").mockRejectedValueOnce(new Error("Read unavailable")).mockResolvedValue(snapshot(2));
  expect(await readDocumentSnapshot(client, "workspace", "document", () => true)).toBe("Read unavailable");
  expect(client.getQueryData<SaveDocument>(key)!.state.revision).toBe(1);
  expect(await readDocumentSnapshot(client, "workspace", "document", () => true)).toBeUndefined();
  expect(client.getQueryData<SaveDocument>(key)!.state.revision).toBe(2);
});
