import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { TitleInfo } from "../../api/protocol";
import type { LibraryGame } from "../../lib/library";
import { formatBytes } from "../../lib/values";
import { library, useLibrary, type LibraryState } from "../../state/libraryStore";
import { useImportTask } from "./importTask";

const supportText = { Edit: "Editable", View: "View only", None: "Raw view only" } as const;

/** Games found in the folder chosen in this tab. Nothing is uploaded until a game is opened. */
export function GameLibrary({ onChooseFolder }: { onChooseFolder: () => void }) {
  const state = useLibrary();
  const titles = useQuery({ queryKey: ["titles"], queryFn: api.titles, staleTime: Infinity });
  const byId = useMemo(() => new Map((titles.data ?? []).map((t) => [t.titleId, t])), [titles.data]);

  return (
    <>
    <p role="status" aria-label="Folder scan status" aria-atomic="true" className="visually-hidden">
      {state.status === "empty" || state.status === "error" ? "" : describe(state.status, state.games.length, state.skipped, state.persistent)}
    </p>
    {state.status !== "empty" && <section className="library" aria-labelledby="library-title">
      <LibraryHeader state={state} onChooseFolder={onChooseFolder} />
      {state.error && <p role="alert" className="notice notice--Danger">{state.error}</p>}
      {state.status === "ready" && state.games.length === 0 && (
        <p className="notice notice--Muted">
          No save dumps found. Pick a folder that contains PPSAxxxxx folders with sdimg_ directories, or save-dump zips.
        </p>
      )}
      <ul className="library__grid">
        {state.games.map((game) => (
          <GameTile key={`${state.snapshotId}:${game.key}`} snapshotId={state.snapshotId} game={game} title={game.titleId ? byId.get(game.titleId) : undefined} icon={state.icons[game.key]} />
        ))}
      </ul>
    </section>}
    </>
  );
}

function describe(status: string, count: number, skipped: number, persistent: boolean): string {
  if (status === "scanning") return "Looking for save dumps…";
  if (status === "reconnect") return "This tab used this folder before. The browser needs your permission to read it again.";
  const found = count === 1 ? "1 game" : `${count} games`;
  const rest = skipped === 0 ? "" : skipped === 1 ? ", 1 other file ignored" : `, ${skipped} other files ignored`;
  return `${found}${rest}. ${persistent ? "Remembered in this tab." : "Choose the folder again after reloading this page."}`;
}

function GameTile({ game, title, icon, snapshotId }: { game: LibraryGame; title: TitleInfo | undefined; icon: string | undefined; snapshotId: number }) {
  const name = title?.name ?? game.fallbackName;
  const support = title?.support ?? "None";
  const { progress, error, task, open } = useGameImport(game, name, snapshotId);
  const busy = progress !== null;
  return (
    <li className={`game${busy ? " game--busy" : ""}`}>
      <div className="game__content">
      <button type="button" className="game__button" onClick={() => void open()} disabled={task.busy} aria-describedby={`${game.key}-meta`}>
        {icon ? <img className="game__art" src={icon} alt="" /> : <span className="game__art game__art--empty">{game.titleId ?? "?"}</span>}
        <span className="game__name">{name}</span>
        <span className="game__meta" id={`${game.key}-meta`}>
          <span className="mono">{game.titleId ?? "unknown title"}</span>
          <span>
            {game.saveDirs === 1 ? "1 save folder" : `${game.saveDirs} save folders`}, {formatBytes(game.totalBytes)}
            {game.kind === "zip" ? ", zip" : ""}
          </span>
        </span>
        <span className={`tag tag--support-${support.toLowerCase()}`}>{supportText[support]}</span>
      </button>
      {busy && <span className="game__progress" role="progressbar" aria-label={`Importing ${name}`} aria-valuenow={Math.round(progress * 100)} aria-valuemin={0} aria-valuemax={100}>
        <span style={{ transform: `scaleX(${progress})` }} />
      </span>}
      </div>
      <span role="status" aria-label={`${name} import status`} aria-atomic="true" className="visually-hidden">
        {busy ? `Importing ${name}: ${Math.round(progress * 100)} %` : ""}
      </span>
      {error && <p role="alert" className="field__error">{error}</p>}
    </li>
  );
}

function LibraryHeader({ state, onChooseFolder }: { state: LibraryState; onChooseFolder: () => void }) {
  return (
    <header className="library__head">
      <div>
        <h2 className="library__title" id="library-title">
          {state.folderName ?? "Folder"}
        </h2>
        <p className="library__meta">{describe(state.status, state.games.length, state.skipped, state.persistent)}</p>
      </div>
      <div className="library__actions">
        {state.status === "reconnect" && (
          <button type="button" className="btn btn--primary" onClick={() => void library.reconnect()}>
            Reopen folder
          </button>
        )}
        {state.persistent && state.status === "ready" && (
          <button type="button" className="btn btn--small btn--quiet" onClick={() => void library.reconnect()}>
            Rescan
          </button>
        )}
        <button type="button" className="btn btn--small" onClick={onChooseFolder}>
          Choose another folder
        </button>
        <button type="button" className="btn btn--small btn--quiet" onClick={() => void library.forget()}>
          Forget folder
        </button>
      </div>
    </header>
  );
}

function useGameImport(game: LibraryGame, name: string, snapshotId: number) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const task = useImportTask(`${snapshotId}:${game.key}`);

  const open = () => task.run(async taskIsCurrent => {
    const isCurrent = () => taskIsCurrent() && library.get().snapshotId === snapshotId;
    if (!isCurrent()) return;
    setError(null);
    setProgress(0);
    try {
      const existing = library.importedWorkspace(game.key, snapshotId);
      if (existing) {
        const alive = await api.workspace(existing).then(() => true, () => false);
        if (!isCurrent()) return;
        if (alive) return navigate(`/w/${existing}`);
      }
      const ws = await api.upload(game.sources, name, progress => { if (isCurrent()) setProgress(progress); });
      void client.invalidateQueries({ queryKey: ["workspaces"] });
      // The snapshot owns reuse; source-selection intent separately owns navigation/feedback.
      library.rememberImport(game.key, ws.id, snapshotId);
      if (!isCurrent()) return;
      void navigate(`/w/${ws.id}`);
    } catch (e) {
      if (!isCurrent()) return;
      setError(e instanceof ApiError ? e.message : "The import failed.");
    } finally {
      // This tile owns only its local progress; even a retired observer must release it.
      setProgress(null);
    }
  });

  return { progress, error, task, open };
}
