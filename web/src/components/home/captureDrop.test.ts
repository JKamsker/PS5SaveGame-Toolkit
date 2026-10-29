import { expect, it, vi } from "vitest";
import { deferred } from "../../test/deferred";
import { captureDrop } from "./captureDrop";

const file = new File(["synthetic"], "save.dat");
const transfer = (value: object) => value as DataTransfer;
function fileEntry() {
  return { isDirectory: false, isFile: true, name: file.name, file: vi.fn((resolve: (file: File) => void) => resolve(file)) };
}
function directory() {
  return { isDirectory: true, isFile: false, name: "Synthetic", createReader: vi.fn(() => ({
    readEntries: (resolve: (entries: object[]) => void) => resolve([]),
  })) };
}

it.each([{}, { items: [] }, { items: [{ webkitGetAsEntry: () => null }], files: [] }])("ignores an empty/non-file payload (%j)", value => {
  expect(captureDrop(transfer(value))).toEqual({ kind: "empty" });
});

it("captures file entries without reading them before upload admission", async () => {
  const entry = fileEntry();
  const captured = captureDrop(transfer({ items: [{ webkitGetAsEntry: () => entry }] }));
  expect(entry.file).not.toHaveBeenCalled(); expect(captured.kind).toBe("files");
  if (captured.kind !== "files") throw new Error("Expected captured files");
  expect(await captured.read()).toEqual([{ file, path: file.name }]);
  expect(entry.file).toHaveBeenCalledOnce();
});

it("captures a FileList-only payload synchronously with its relative paths", async () => {
  const nested = new File(["synthetic nested"], "nested.dat");
  Object.defineProperty(nested, "webkitRelativePath", { value: "Folder/nested.dat" });
  const value = { items: [{ kind: "file" }], files: [nested] };
  const captured = captureDrop(transfer(value));
  value.files = []; // Browser drop data can disappear after the event returns.
  expect(captured.kind).toBe("files");
  if (captured.kind !== "files") throw new Error("Expected captured files");
  expect(await captured.read()).toEqual([{ file: nested, path: "Folder/nested.dat" }]);
});

it("accepts an array-like item list without an iterator", async () => {
  const captured = captureDrop(transfer({ items: { length: 1, 0: { webkitGetAsEntry: () => fileEntry() } } }));
  expect(captured.kind).toBe("files");
  if (captured.kind !== "files") throw new Error("Expected captured files");
  expect(await captured.read()).toEqual([{ path: file.name, file }]);
});

it("retains directory evidence if a later array-like item getter throws", async () => {
  const error = new DOMException("Synthetic item failure", "NotReadableError");
  const entry = directory();
  const captured = captureDrop(transfer({ items: { length: 2, 0: { webkitGetAsEntry: () => entry }, get 1() { throw error; } } }));
  expect(captured.kind).toBe("folder");
  if (captured.kind !== "folder") throw new Error("Expected captured folder");
  await expect(captured.read()).rejects.toBe(error); expect(entry.createReader).not.toHaveBeenCalled();
});

it("keeps the native handle associated with the directory after a preceding non-file item", async () => {
  const entry = directory(); const pending = deferred<FileSystemHandle | null>();
  const acquire = vi.fn(() => pending.promise);
  const captured = captureDrop(transfer({ items: [
    { webkitGetAsEntry: () => null }, { webkitGetAsEntry: () => entry, getAsFileSystemHandle: acquire },
  ] }));
  expect(acquire).toHaveBeenCalledOnce(); expect(entry.createReader).not.toHaveBeenCalled();
  expect(captured.kind).toBe("folder");
  if (captured.kind !== "folder") throw new Error("Expected captured folder");
  const handle = { kind: "directory", name: "Native", values: async function* () {
    yield { kind: "file", name: file.name, getFile: async () => file };
  } } as unknown as FileSystemDirectoryHandle;
  const read = captured.read(); pending.resolve(handle);
  expect(await read).toEqual({ name: "Native", handle, files: [{ path: file.name, file }] });
  expect(entry.createReader).not.toHaveBeenCalled();
});

it("traverses mixed entries completely through the fallback instead of using a partial native handle", async () => {
  const entry = fileEntry(); const folder = directory(); const acquire = vi.fn();
  const captured = captureDrop(transfer({ items: [
    { webkitGetAsEntry: () => folder, getAsFileSystemHandle: acquire }, { webkitGetAsEntry: () => entry },
  ] }));
  expect(acquire).not.toHaveBeenCalled(); expect(entry.file).not.toHaveBeenCalled();
  expect(captured.kind).toBe("folder");
  if (captured.kind !== "folder") throw new Error("Expected captured folder");
  expect(await captured.read()).toEqual({ name: "Dropped folders", files: [{ path: file.name, file }] });
  expect(folder.createReader).toHaveBeenCalledOnce(); expect(entry.file).toHaveBeenCalledOnce();
});

it.each([false, true])("preserves a known folder when native handle acquisition throws/rejects (async=%s)", async asynchronous => {
  const error = new DOMException("Synthetic handle failure", "NotReadableError");
  const entry = directory();
  const captured = captureDrop(transfer({ items: [{ webkitGetAsEntry: () => entry, getAsFileSystemHandle: () => {
    if (asynchronous) return Promise.reject(error);
    throw error;
  } }] }));
  expect(captured.kind).toBe("folder");
  if (captured.kind !== "folder") throw new Error("Expected captured folder");
  await expect(captured.read()).rejects.toBe(error);
  expect(entry.createReader).not.toHaveBeenCalled();
});

it.each([false, true])("reports an unsupported entry as incomplete classification (known folder=%s)", async knownFolder => {
  const entries = knownFolder ? [directory(), { isDirectory: false, isFile: false }] : [{ isDirectory: false, isFile: false }];
  const captured = captureDrop(transfer({ items: entries.map(entry => ({ webkitGetAsEntry: () => entry })) }));
  expect(captured.kind).toBe(knownFolder ? "folder" : "unreadable");
  if (captured.kind === "folder") await expect(captured.read()).rejects.toThrow("Unrecognized dropped entry.");
});
