import { useState } from "react";
import { api } from "../../api/client";
import type { SaveDirectory, SaveFileInfo, Workspace } from "../../api/protocol";
import { formatBytes } from "../../lib/values";

interface SlotRailProps {
  workspace: Workspace;
  activeFileId: string | undefined;
  onOpen: (file: SaveFileInfo) => void;
}

/** Save directories as slots with their console key art; files of the selected slot unfold below. */
export function SlotRail({ workspace, activeFileId, onOpen }: SlotRailProps) {
  const [showBackups, setShowBackups] = useState(false);
  const dirtyFiles = new Set(workspace.documents.filter((d) => d.isDirty)
    .flatMap((d) => d.groupMembers?.map(member => member.fileId) ?? [d.fileId]));
  const activeDir = workspace.titles.flatMap((t) => t.directories).find((d) => d.files.some((f) => f.id === activeFileId));
  const [expanded, setExpanded] = useState<string | undefined>(activeDir?.id);
  const hiddenBackups = workspace.titles.flatMap((t) => t.directories).filter((d) => d.isBackup && d.twinIdentical).length;

  return (
    <nav className="rail" aria-label="Save directories">
      {hiddenBackups > 0 && (
        <label className="rail__toggle">
          <input type="checkbox" checked={showBackups} onChange={(e) => setShowBackups(e.target.checked)} />
          Show {hiddenBackups} identical console backups
        </label>
      )}
      {workspace.titles.map((title) => (
        <section key={title.titleId}>
          <header className="rail__title">
            <h2 className="rail__game">{title.gameName ?? title.titleId}</h2>
            {title.gameName && <span className="rail__titleid mono">{title.titleId}</span>}
          </header>
          {groupByGeneration(title.directories.filter((d) => showBackups || !(d.isBackup && d.twinIdentical))).map(([generation, dirs]) => (
            <div key={generation} className="rail__generation">
              {generation > 0 && (
                <h3 className="rail__copy" title="The archive contained these paths more than once. Copy order is not necessarily chronological.">
                  Archive copy {generation + 1}
                </h3>
              )}
              {dirs.map((dir) => (
                <Slot
                  key={dir.id}
                  workspaceId={workspace.id}
                  dir={dir}
                  expanded={expanded === dir.id}
                  dirty={dir.files.some((f) => dirtyFiles.has(f.id))}
                  activeFileId={activeFileId}
                  onToggle={() => setExpanded(expanded === dir.id ? undefined : dir.id)}
                  onOpen={onOpen}
                />
              ))}
            </div>
          ))}
        </section>
      ))}
    </nav>
  );
}

function groupByGeneration(dirs: SaveDirectory[]): [number, SaveDirectory[]][] {
  const groups = new Map<number, SaveDirectory[]>();
  for (const d of dirs) groups.set(d.generation, [...(groups.get(d.generation) ?? []), d]);
  return [...groups.entries()].sort(([a], [b]) => a - b);
}

interface SlotProps {
  workspaceId: string;
  dir: SaveDirectory;
  expanded: boolean;
  dirty: boolean;
  activeFileId: string | undefined;
  onToggle: () => void;
  onOpen: (file: SaveFileInfo) => void;
}

function Slot({ workspaceId, dir, expanded, dirty, activeFileId, onToggle, onOpen }: SlotProps) {
  // Placeholder param.sfo files carry no text; the best game plugin's label is the next best description.
  const pluginLabel = dir.files.map((f) => f.matches[0]).find((m) => m && m.score > 1)?.label;
  const subtitle = dir.sfo?.subTitle || dir.sfo?.detail || pluginLabel;
  const classes = ["slot", expanded ? "slot--active" : "", dirty ? "slot--modified" : ""].filter(Boolean).join(" ");
  return (
    <div className={classes}>
      <button type="button" className="slot__head" aria-expanded={expanded} onClick={onToggle}>
        {dir.hasIcon ? (
          <img className="slot__art" src={api.iconUrl(workspaceId, dir.id)} alt="" loading="lazy" width={52} height={52} />
        ) : (
          <span className="slot__art slot__art--empty">{dir.files.length}</span>
        )}
        <span style={{ minWidth: 0 }}>
          <span className="slot__name" title={dir.name} style={{ display: "block" }}>
            {dir.name}
          </span>
          {subtitle && <span className="slot__subtitle" title={subtitle} style={{ display: "block" }}>{subtitle}</span>}
          <span className="slot__meta">
            <span>{dir.files.length === 1 ? "1 file" : `${dir.files.length} files`}</span>
            {dir.isBackup && <span>console backup{dir.twinIdentical ? ", identical" : ", differs"}</span>}
            {!dir.isBackup && dir.twinId && <span>{dir.twinIdentical ? "backup identical" : "backup differs"}</span>}
          </span>
        </span>
      </button>
      {expanded && (
        <SlotFiles files={dir.files} activeFileId={activeFileId} onOpen={onOpen} />
      )}
    </div>
  );
}

function SlotFiles({ files, activeFileId, onOpen }: { files: SaveFileInfo[]; activeFileId: string | undefined; onOpen: (file: SaveFileInfo) => void }) {
  return (
    <ul className="slot__files">
      {files.map((f) => {
        const best = f.matches[0];
        const isGame = best !== undefined && best.score > 1;
        return (
          <li key={f.id}>
            <button type="button" className={`file${f.id === activeFileId ? " file--open" : ""}`} onClick={() => onOpen(f)}>
              <span style={{ minWidth: 0 }}>
                <span className="file__name mono" title={f.path} style={{ display: "block" }}>
                  {f.path}
                </span>
                <span className={`file__plugin${isGame ? " file__plugin--game" : ""}`}>
                  {best ? (best.label ? `${best.label}` : best.pluginName) : "No plugin"}
                </span>
              </span>
              <span className="file__size">{formatBytes(f.length)}</span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}
