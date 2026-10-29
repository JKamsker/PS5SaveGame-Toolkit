import { useEffect, useRef, useState, type DragEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { Plugin } from "../../api/protocol";
import { library } from "../../state/libraryStore";
import { fromInput, walkEntries, type UploadFile } from "./collectFiles";
import { GameLibrary } from "./GameLibrary";
import { AppVersion } from "../ui/AppVersion";
import { useEntryFocus } from "../ui/useEntryFocus";
import { useAsyncTask } from "../ui/useAsyncTask";
import { ImportTaskContext } from "./importTask";

const maturityText = { ReadOnly: "view only", Experimental: "editing, untested on console", Stable: "editing, tested in-game" } as const;

export function HomePage() {
  const focusHeading = useEntryFocus("home");
  const navigate = useNavigate();
  const importTask = useAsyncTask();
  const [dragging, setDragging] = useState(false);
  const [status, setStatus] = useState<{ text: string; isError: boolean } | null>(null);
  const [failedUpload, setFailedUpload] = useState<UploadFile[]>([]);
  const zipInput = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const plugins = useQuery({ queryKey: ["plugins"], queryFn: api.plugins });
  const recent = useQuery({ queryKey: ["workspaces"], queryFn: api.workspaces });

  useEffect(() => void library.restore(), []);

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
        if (isCurrent()) navigate(`/w/${ws.id}`);
      } catch (error) {
        if (!isCurrent()) return;
        setFailedUpload(files);
        setStatus({ text: error instanceof ApiError ? error.message : "The import failed. Is the server running?", isError: true });
      }
    });
  };

  const chooseFolder = async () => {
    setStatus(null);
    if (!(library.supportsHandles() && (await library.pickFolder()))) folderInput.current?.click();
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    // DataTransfer is only readable synchronously inside the event, so grab entries/handles first.
    const items = Array.from(e.dataTransfer.items);
    const entries = items.map((i) => i.webkitGetAsEntry?.()).filter((x): x is FileSystemEntry => !!x);
    const handle = entries.length === 1 && entries[0]!.isDirectory ? items[0]?.getAsFileSystemHandle?.() : undefined;
    void (async () => {
      if (!entries.some((x) => x.isDirectory)) return upload(await walkEntries(entries));
      const resolved = await handle;
      if (resolved?.kind === "directory") return library.scanHandle(resolved as FileSystemDirectoryHandle);
      return library.scanFiles(entries.length === 1 ? entries[0]!.name : "Dropped folders", await walkEntries(entries));
    })();
  };

  return (
    <ImportTaskContext.Provider value={importTask}><main className="home">
      <div className="home__brand">
        <span className="bar__mark" aria-hidden="true" />
        Savebench
      </div>
      <GameLibrary onChooseFolder={() => void chooseFolder()} />
      <div className="home__grid">
        <div>
          <h1 ref={focusHeading} tabIndex={-1} className="home__headline">Open a PS5 save dump</h1>
          <p className="home__lede">
            Choose the folder where you keep your decrypted dumps to see every game in it, or drop a single zip. Everything stays on this computer
            until you open a game.
          </p>
          <div
            className={`drop${dragging ? " drop--active" : ""}`}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={onDrop}
          >
            <p className="drop__title">Drop a folder or zip here</p>
            <p className="drop__hint">Folders are scanned in the browser for PPSAxxxxx/sdimg_… dumps and save-dump zips. A zip dropped here is opened right away.</p>
            <div className="drop__buttons">
              <button type="button" className="btn btn--primary" disabled={importTask.busy} onClick={() => void chooseFolder()}>
                Choose folder
              </button>
              <button type="button" className="btn" disabled={importTask.busy} onClick={() => zipInput.current?.click()}>
                Open zip
              </button>
            </div>
            {status && <p className={status.isError ? "notice notice--Danger" : "drop__status"} role={status.isError ? "alert" : "status"}>{status.text}</p>}
            {status?.isError && failedUpload.length > 0 && <button type="button" className="btn btn--small" onClick={() => void upload(failedUpload)}>Retry import</button>}
            <input ref={zipInput} type="file" accept=".zip" multiple hidden disabled={importTask.busy} onChange={(e) => {
              const files = e.target.files ? fromInput(e.target.files) : [];
              e.target.value = "";
              void upload(files);
            }} />
            <input
              ref={folderInput}
              type="file"
              hidden
              multiple
              {...{ webkitdirectory: "", directory: "" }}
              onChange={(e) => {
                const files = e.target.files;
                if (!files || files.length === 0) return;
                const root = files[0]!.webkitRelativePath.split("/")[0] || "Folder";
                void library.scanFiles(root, fromInput(files));
              }}
            />
          </div>
          {recent.isError && <div>
            <p role="alert" className="notice notice--Danger">Could not load open dumps: {recent.error.message}</p>
            <button type="button" className="btn btn--small" disabled={recent.isFetching} onClick={() => void recent.refetch()}>Retry open dumps</button>
          </div>}
          {recent.data && recent.data.length > 0 && (
            <section className="recent">
              <h2>Open dumps</h2>
              <ul>
                {recent.data.map((w) => (
                  <li key={w.id}>
                    <Link to={`/w/${w.id}`}>
                      <span className="recent__name">{w.name}</span>
                      <span className="recent__meta">
                        {w.titleIds.join(", ")}, {w.directoryCount} folders
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>
        <div>
          {plugins.isError && <div>
            <p role="alert" className="notice notice--Danger">Could not load supported games: {plugins.error.message}</p>
            <button type="button" className="btn btn--small" disabled={plugins.isFetching} onClick={() => void plugins.refetch()}>Retry supported games</button>
          </div>}
          <GameList plugins={plugins.data} />
        </div>
      </div>
      <AppVersion />
    </main></ImportTaskContext.Provider>
  );
}

function GameList({ plugins }: { plugins: Plugin[] | undefined }) {
  if (!plugins) return null;
  const games = plugins.filter((p) => p.games.length > 0);
  const generic = plugins.filter((p) => p.games.length === 0);
  return (
    <aside className="games">
      <h2>Supported games</h2>
      {games.length === 0 && <p className="games__note">No game plugins are installed yet. Files still open in the raw inspector.</p>}
      <ul>
        {games.map((p) => (
          <li key={p.id}>
            <span className="games__name">{p.name}</span>
            <span className={`tag tag--${p.maturity.toLowerCase()}`}>{maturityText[p.maturity]}</span>
            <span className="games__titles mono">{p.games.map((g) => g.titleId).join(" ")}</span>
          </li>
        ))}
      </ul>
      {generic.length > 0 && <p className="games__note">Also understood for any game: {generic.map((p) => p.name).join(", ")}.</p>}
    </aside>
  );
}
