export interface UploadFile {
  file: File;
  path: string;
}

/** Reads dropped items, walking dropped folders recursively so relative paths survive the upload. */
export async function collectDropped(items: DataTransferItemList): Promise<UploadFile[]> {
  const entries = Array.from(items)
    .map((i) => i.webkitGetAsEntry?.())
    .filter((e): e is FileSystemEntry => e !== null && e !== undefined);
  const out: UploadFile[] = [];
  for (const entry of entries) await walk(entry, "", out);
  return out;
}

async function walk(entry: FileSystemEntry, prefix: string, out: UploadFile[]): Promise<void> {
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

export function fromInput(files: FileList): UploadFile[] {
  return Array.from(files).map((file) => ({ file, path: file.webkitRelativePath || file.name }));
}
