import { useRef, useState, type DragEvent } from "react";
import { useNavigate } from "react-router";
import { api, ApiError } from "../../api/client";
import { library } from "../../state/libraryStore";
import { walkEntries, type UploadFile } from "./collectFiles";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";

function useHomeUpload() {
  const navigate = useNavigate();
  const importTask = useAsyncTask();
  const [status, setStatus] = useState<{ text: string; isError: boolean } | null>(null);
  const [failedUpload, setFailedUpload] = useState<UploadFile[]>([]);
  const upload = (files: UploadFile[]) => {
    if (files.length === 0) return;
    return importTask.run(async isCurrent => {
      setFailedUpload([]);
      const total = files.reduce((n, f) => n + f.file.size, 0);
      const label = files.length === 1 ? files[0]?.path : `${files.length} files`;
      setStatus({ text: `Importing ${label} (${Math.round(total / 1048576)} MiB)…`, isError: false });
      try {
        const ws = await api.upload(files, undefined, (p) =>
          { if (isCurrent()) setStatus({ text: `Importing ${label}: ${Math.round(p * 100)} %`, isError: false }); },
        );
        if (isCurrent()) void navigate(`/w/${ws.id}`);
      } catch (error) {
        if (!isCurrent()) return;
        setFailedUpload(files);
        setStatus({ text: error instanceof ApiError ? error.message : "The import failed. Is the server running?", isError: true });
      }
    });
  };

  return { importTask, status, setStatus, failedUpload, upload };
}

/** Folder selection invalidates older drop enumeration without changing the shared import task. */
export function useHomeImport() {
  const { importTask, status, setStatus, failedUpload, upload } = useHomeUpload();
  const beginFolderSelection = useLatestRequest("home-folder");
  const [dragging, setDragging] = useState(false);
  const zipInput = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const chooseFolder = async () => {
    const isCurrent = beginFolderSelection();
    setStatus(null);
    const handled = library.supportsHandles() && await library.pickFolder(isCurrent);
    if (!handled && isCurrent()) folderInput.current?.click();
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const isCurrent = beginFolderSelection();
    // DataTransfer is only readable synchronously inside the event, so grab entries/handles first.
    const items = Array.from(e.dataTransfer.items);
    const entries = items.map((i) => i.webkitGetAsEntry?.()).filter((x): x is FileSystemEntry => !!x);
    const handle = entries.length === 1 && entries[0]!.isDirectory ? items[0]?.getAsFileSystemHandle?.() : undefined;
    void (async () => {
      if (!entries.some((x) => x.isDirectory)) {
        const files = await walkEntries(entries);
        if (isCurrent()) return upload(files);
        return;
      }
      const resolved = await handle;
      if (!isCurrent()) return;
      if (resolved?.kind === "directory") return library.scanHandle(resolved as FileSystemDirectoryHandle);
      const files = await walkEntries(entries);
      if (isCurrent()) return library.scanFiles(entries.length === 1 ? entries[0]!.name : "Dropped folders", files);
    })();
  };

  return { importTask, status, failedUpload, upload, beginFolderSelection, dragging, setDragging, zipInput, folderInput, chooseFolder, onDrop };
}
