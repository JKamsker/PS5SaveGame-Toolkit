import type { ScannedFile } from "../../lib/library";

export type UploadFile = ScannedFile;

/** Walks dropped entries recursively so relative paths survive (folder drops). */
export async function walkEntries(entries: FileSystemEntry[]): Promise<ScannedFile[]> {
  const out: ScannedFile[] = [];
  for (const entry of entries) await walk(entry, "", out);
  return out;
}

async function walk(entry: FileSystemEntry, prefix: string, out: ScannedFile[]): Promise<void> {
  if (entry.isFile) {
    const file = await new Promise<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject));
    out.push({ file, path: prefix + entry.name });
    return;
  }
  if (!entry.isDirectory) return;
  const reader = (entry as FileSystemDirectoryEntry).createReader();
  // readEntries returns results in batches; keep reading until empty.
  for (;;) {
    const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject));
    if (batch.length === 0) break;
    for (const child of batch) await walk(child, `${prefix}${entry.name}/`, out);
  }
}

export function fromInput(files: FileList): ScannedFile[] {
  return Array.from(files).map((file) => ({ file, path: file.webkitRelativePath || file.name }));
}
