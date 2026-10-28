import { useRef, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { Plugin } from "../../api/protocol";
import { collectDropped, fromInput, type UploadFile } from "./collectFiles";

const maturityText = { ReadOnly: "view only", Experimental: "editing, untested on console", Stable: "editing, tested in-game" } as const;

export function HomePage() {
  const navigate = useNavigate();
  const [dragging, setDragging] = useState(false);
  const [status, setStatus] = useState<{ text: string; isError: boolean } | null>(null);
  const zipInput = useRef<HTMLInputElement>(null);
  const folderInput = useRef<HTMLInputElement>(null);
  const plugins = useQuery({ queryKey: ["plugins"], queryFn: api.plugins });
  const recent = useQuery({ queryKey: ["workspaces"], queryFn: api.workspaces });

  const upload = async (files: UploadFile[]) => {
    if (files.length === 0) return;
    const total = files.reduce((n, f) => n + f.file.size, 0);
    setStatus({ text: `Importing ${files.length === 1 ? files[0]?.path : `${files.length} files`} (${Math.round(total / 1048576)} MiB)…`, isError: false });
    try {
      const ws = await api.upload(files);
      navigate(`/w/${ws.id}`);
    } catch (error) {
      setStatus({ text: error instanceof ApiError ? error.message : "The import failed. Is the server running?", isError: true });
    }
  };

  return (
    <div className="home">
      <div className="home__brand">
        <span className="bar__mark" aria-hidden="true" />
        Savebench
      </div>
      <div className="home__grid">
        <div>
          <h1 className="home__headline">Open a PS5 save dump</h1>
          <p className="home__lede">
            Drop the decrypted zip from your save manager or a folder of <span className="mono">sdimg_</span> directories. Everything stays on this computer.
          </p>
          <div
            className={`drop${dragging ? " drop--active" : ""}`}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={async (e) => {
              e.preventDefault();
              setDragging(false);
              await upload(await collectDropped(e.dataTransfer.items));
            }}
          >
            <p className="drop__title">Drop a zip or folder here</p>
            <p className="drop__hint">Layout like PPSA02530/sdimg_SAVEDATA/… is detected automatically, including the console backup copies.</p>
            <div className="drop__buttons">
              <button type="button" className="btn btn--primary" onClick={() => zipInput.current?.click()}>
                Choose zip
              </button>
              <button type="button" className="btn" onClick={() => folderInput.current?.click()}>
                Choose folder
              </button>
            </div>
            {status && <p className={status.isError ? "notice notice--Danger" : "drop__status"} role="status">{status.text}</p>}
            <input ref={zipInput} type="file" accept=".zip" multiple hidden onChange={(e) => e.target.files && void upload(fromInput(e.target.files))} />
            <input
              ref={folderInput}
              type="file"
              hidden
              multiple
              {...{ webkitdirectory: "", directory: "" }}
              onChange={(e) => e.target.files && void upload(fromInput(e.target.files))}
            />
          </div>
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
        <GameList plugins={plugins.data} />
      </div>
    </div>
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
