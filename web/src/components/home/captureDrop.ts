import { listDirectory } from "../../lib/library";
import { fromInput, walkEntries, type UploadFile } from "./collectFiles";

interface DroppedFolder { name: string; files: UploadFile[]; handle?: FileSystemDirectoryHandle }
export type CapturedDrop =
  | { kind: "empty" }
  | { kind: "files"; read: () => Promise<UploadFile[]> }
  | { kind: "folder"; read: () => Promise<DroppedFolder> }
  | { kind: "unreadable" };

/** Support both iterable and older array-like item lists without eagerly consuming metadata. */
function* droppedItems(items: DataTransferItemList | undefined): Generator<DataTransferItem> {
  if (!items) return;
  if (typeof items[Symbol.iterator] === "function") yield* items;
  else for (let index = 0; index < items.length; index++) yield items[index]!;
}

/** Capture event-only metadata synchronously; do not read file contents or adopt partial sources. */
export function captureDrop(transfer: DataTransfer): CapturedDrop {
  let containsFolder = false;
  try {
    const entries: FileSystemEntry[] = [];
    let firstEntryItem: DataTransferItem | undefined;
    // Read incrementally: later item/iterator/getter failures cannot erase an observed directory.
    for (const item of droppedItems(transfer.items)) {
      const entry = item.webkitGetAsEntry?.();
      if (!entry) continue;
      entries.push(entry);
      if (entry.isDirectory) containsFolder = true;
      else if (!entry.isFile) throw new Error("Unrecognized dropped entry.");
      if (entries.length === 1) firstEntryItem = item;
    }
    if (containsFolder) {
      // Handle acquisition must also start within the drop event, before its first await.
      const handle = entries.length === 1 ? firstEntryItem?.getAsFileSystemHandle?.() : undefined;
      return { kind: "folder", read: async () => {
        const resolved = await handle;
        if (resolved?.kind === "directory") {
          const directory = resolved as FileSystemDirectoryHandle;
          return { name: directory.name, handle: directory, files: await listDirectory(directory) };
        }
        return { name: entries.length === 1 ? entries[0]!.name : "Dropped folders", files: await walkEntries(entries) };
      } };
    }
    const files = entries.length === 0 && transfer.files ? fromInput(transfer.files) : [];
    if (entries.length === 0 && files.length === 0) return { kind: "empty" };
    return { kind: "files", read: () => entries.length > 0 ? walkEntries(entries) : Promise.resolve(files) };
  } catch (failure) {
    if (containsFolder) return { kind: "folder", read: async () => { throw failure; } };
    // Incomplete metadata cannot establish a file-only payload. Report it independently of
    // upload admission instead of silently dropping an error behind an occupied import slot.
    return { kind: "unreadable" };
  }
}
