import { useRef, useState, type DragEvent } from "react";
import { useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import { library, useLibrary } from "../../state/libraryStore";
import type { UploadFile } from "./collectFiles";
import { captureDrop } from "./captureDrop";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";

const droppedReadFailure = "Could not read the dropped folder or files. Check that you have access, then use Choose folder or drop them again.";

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
  const startUpload = (run: (isCurrent: () => boolean) => Promise<void>) => {
    return importTask.run(async taskIsCurrent => {
      // Acquire observer ownership only after the shared submit guard accepts this upload.
      const sourceIsCurrent = beginSelection();
      const isCurrent = () => taskIsCurrent() && sourceIsCurrent() && library.get().snapshotId === snapshotId;
      if (isCurrent()) await run(isCurrent);
    });
  };
  const sendUpload = async (files: UploadFile[], isCurrent: () => boolean) => {
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
  };
  const upload = (files: UploadFile[]) => files.length > 0 ? startUpload(isCurrent => sendUpload(files, isCurrent)) : undefined;
  const uploadDroppedFiles = (read: () => Promise<UploadFile[]>) => startUpload(async isCurrent => {
    // Reserve the submit slot before reading file entries. Rejected drops must not change intent or feedback.
    setFailedUpload([]);
    setStatus({ text: "Reading dropped files…", isError: false });
    try {
      const files = await read();
      if (!isCurrent()) return;
      if (files.length > 0) await sendUpload(files, isCurrent);
      else setStatus(null);
    } catch {
      if (isCurrent()) setStatus({ text: droppedReadFailure, isError: true });
    }
  });

  return { importTask, status, setStatus, failedUpload, setFailedUpload, upload, uploadDroppedFiles };
}

/** Source selection retires older observers without releasing the shared import submit slot. */
export function useHomeImport() {
  const beginRequest = useLatestRequest("home-folder");
  const { importTask, status, setStatus, failedUpload, setFailedUpload, upload, uploadDroppedFiles } = useHomeUpload(beginRequest);
  const [dragging, setDragging] = useState(false);
  const zipInput = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const beginSourceSelection = () => {
    setStatus(null);
    setFailedUpload([]);
    return beginRequest();
  };
  const chooseFolder = async () => {
    const isCurrent = beginSourceSelection();
    const handled = library.supportsHandles() && await library.pickFolder(isCurrent);
    if (!handled && isCurrent()) folderInput.current?.click();
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const source = captureDrop(e.dataTransfer);
    if (source.kind === "empty") return;
    if (source.kind === "files") { void uploadDroppedFiles(source.read); return; }

    // Known folders and unclassified metadata failures own recovery independently of upload
    // admission. Only a fully classified file-only payload may be rejected as a duplicate.
    const requestIsCurrent = beginSourceSelection();
    if (source.kind === "unreadable") { setStatus({ text: droppedReadFailure, isError: true }); return; }
    const snapshotId = library.get().snapshotId;
    const isCurrent = () => requestIsCurrent() && library.get().snapshotId === snapshotId;
    setStatus({ text: "Reading dropped folder…", isError: false });
    void (async () => {
      try {
        const folder = await source.read();
        if (!isCurrent()) return;
        setStatus(null);
        if (folder.handle) await library.scanHandle(folder.handle, folder.files);
        else await library.scanFiles(folder.name, folder.files);
      } catch {
        if (isCurrent()) setStatus({ text: droppedReadFailure, isError: true });
      }
    })();
  };

  return { importTask, status, failedUpload, upload, beginFolderSelection: beginSourceSelection, beginImportIntent: beginSourceSelection,
    dragging, setDragging, zipInput, folderInput, chooseFolder, onDrop };
}
