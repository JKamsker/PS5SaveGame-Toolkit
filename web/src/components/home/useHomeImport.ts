import { useRef, useState, type DragEvent } from "react";
import { useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import { library, useLibrary } from "../../state/libraryStore";
import { listDirectory } from "../../lib/library";
import { walkEntries, type UploadFile } from "./collectFiles";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";

function useHomeUpload(beginSelection: () => () => boolean) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const { snapshotId } = useLibrary();
  const importTask = useAsyncTask(snapshotId);
  type Status = { text: string; isError: boolean };
  const [feedback, setFeedback] = useState<{ snapshotId: number; status: Status | null }>({ snapshotId, status: null });
  const [failed, setFailed] = useState<{ snapshotId: number; files: UploadFile[] }>({ snapshotId, files: [] });
  const setStatus = (status: Status | null) => setFeedback({ snapshotId, status });
  const setFailedUpload = (files: UploadFile[]) => setFailed({ snapshotId, files });
  const status = feedback.snapshotId === snapshotId ? feedback.status : null;
  const failedUpload = failed.snapshotId === snapshotId ? failed.files : [];
  const upload = (files: UploadFile[], selectionIsCurrent?: () => boolean) => {
    if (files.length === 0) return;
    return importTask.run(async taskIsCurrent => {
      // Acquire observer ownership only after the shared submit guard accepts this upload.
      const sourceIsCurrent = selectionIsCurrent ?? beginSelection();
      const isCurrent = () => taskIsCurrent() && sourceIsCurrent() && library.get().snapshotId === snapshotId;
      if (!isCurrent()) return;
      setFailedUpload([]);
      const total = files.reduce((n, f) => n + f.file.size, 0);
      const label = files.length === 1 ? files[0]?.path : `${files.length} files`;
      setStatus({ text: `Importing ${label} (${Math.round(total / 1048576)} MiB)…`, isError: false });
      try {
        const ws = await api.upload(files, undefined, (p) =>
          { if (isCurrent()) setStatus({ text: `Importing ${label}: ${Math.round(p * 100)} %`, isError: false }); },
        );
        void client.invalidateQueries({ queryKey: ["workspaces"] });
        if (isCurrent()) void navigate(`/w/${ws.id}`);
      } catch (error) {
        if (!isCurrent()) return;
        setFailedUpload(files);
        setStatus({ text: error instanceof ApiError ? error.message : "The import failed. Is the server running?", isError: true });
      }
    });
  };

  return { importTask, status, setStatus, failedUpload, setFailedUpload, upload };
}

/** Source selection retires older observers without releasing the shared import submit slot. */
export function useHomeImport() {
  const beginRequest = useLatestRequest("home-folder");
  const { importTask, status, setStatus, failedUpload, setFailedUpload, upload } = useHomeUpload(beginRequest);
  const [dragging, setDragging] = useState(false);
  const zipInput = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const beginFolderSelection = () => {
    setStatus(null);
    setFailedUpload([]);
    return beginRequest();
  };
  const chooseFolder = async () => {
    const isCurrent = beginFolderSelection();
    const handled = library.supportsHandles() && await library.pickFolder(isCurrent);
    if (!handled && isCurrent()) folderInput.current?.click();
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const requestIsCurrent = beginFolderSelection();
    const snapshotId = library.get().snapshotId;
    const isCurrent = () => requestIsCurrent() && library.get().snapshotId === snapshotId;
    void (async () => {
      try {
        // Read DataTransfer before the first await; synchronous access failures need this boundary too.
        const items = Array.from(e.dataTransfer.items);
        const entries = items.map((i) => i.webkitGetAsEntry?.()).filter((x): x is FileSystemEntry => !!x);
        const hasFolder = entries.some(entry => entry.isDirectory);
        setStatus({ text: hasFolder ? "Reading dropped folder…" : "Reading dropped files…", isError: false });
        const handle = entries.length === 1 && entries[0]!.isDirectory ? items[0]?.getAsFileSystemHandle?.() : undefined;
        if (!hasFolder) {
          const files = await walkEntries(entries);
          if (!isCurrent()) return;
          setStatus(null);
          return await upload(files, requestIsCurrent);
        }
        const resolved = await handle;
        if (!isCurrent()) return;
        if (resolved?.kind === "directory") {
          // Finish reading before replacing the previous library; a partial traversal is unusable.
          const files = await listDirectory(resolved as FileSystemDirectoryHandle);
          if (!isCurrent()) return;
          setStatus(null);
          return await library.scanHandle(resolved as FileSystemDirectoryHandle, files);
        }
        const files = await walkEntries(entries);
        if (!isCurrent()) return;
        setStatus(null);
        await library.scanFiles(entries.length === 1 ? entries[0]!.name : "Dropped folders", files);
      } catch {
        if (!isCurrent()) return;
        setStatus({ text: "Could not read the dropped folder or files. Check that you have access, then use Choose folder or drop them again.", isError: true });
      }
    })();
  };

  return { importTask, status, failedUpload, upload, beginFolderSelection, beginImportIntent: beginFolderSelection,
    dragging, setDragging, zipInput, folderInput, chooseFolder, onDrop };
}
