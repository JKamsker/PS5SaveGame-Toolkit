import { useEffect } from "react";
import { Link } from "react-router";
import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Plugin } from "../../api/protocol";
import { library } from "../../state/libraryStore";
import { fromInput } from "./collectFiles";
import { GameLibrary } from "./GameLibrary";
import { AppVersion } from "../ui/AppVersion";
import { useEntryFocus } from "../ui/useEntryFocus";
import { ImportTaskContext } from "./importTask";
import { useHomeImport } from "./useHomeImport";

const maturityText = { ReadOnly: "view only", Experimental: "editing, untested on console", Stable: "editing, tested in-game" } as const;

export function HomePage() {
  const focusHeading = useEntryFocus("home");
  const controller = useHomeImport();
  const plugins = useQuery({ queryKey: ["plugins"], queryFn: api.plugins });
  const recent = useQuery({ queryKey: ["workspaces"], queryFn: api.workspaces });
  useEffect(() => void library.restore(), []);
  return (
    <ImportTaskContext.Provider value={controller.importTask}><main className="home">
      <div className="home__brand">
        <span className="bar__mark" aria-hidden="true" />
        Savebench
      </div>
      <GameLibrary onChooseFolder={() => void controller.chooseFolder()} />
      <div className="home__grid">
        <div>
          <h1 ref={focusHeading} tabIndex={-1} className="home__headline">Open a PS5 save dump</h1>
          <p className="home__lede">
            Choose the folder where you keep your decrypted dumps to see every game in it, or drop a single zip. Everything stays on this computer
            until you open a game.
          </p>
          <ImportDropZone controller={controller} />
          <RecentDumps recent={recent} />
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

function ImportDropZone({ controller }: { controller: ReturnType<typeof useHomeImport> }) {
  const { dragging, setDragging, onDrop, importTask, chooseFolder, zipInput, status, failedUpload, upload } = controller;
  return (
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
      <ImportInputs controller={controller} />
    </div>
  );
}

function ImportInputs({ controller }: { controller: ReturnType<typeof useHomeImport> }) {
  const { zipInput, folderInput, importTask, upload, beginFolderSelection } = controller;
  return <>
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
        beginFolderSelection();
        const root = files[0]!.webkitRelativePath.split("/")[0] || "Folder";
        void library.scanFiles(root, fromInput(files));
      }}
    />
  </>;
}

function RecentDumps({ recent }: { recent: UseQueryResult<Awaited<ReturnType<typeof api.workspaces>>> }) {
  return <>
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
  </>;
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
