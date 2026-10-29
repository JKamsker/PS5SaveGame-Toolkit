import { Link } from "react-router";

interface WorkspaceHeaderProps {
  name: string;
  dirtyCount: number;
  closing: boolean;
  onClose: () => Promise<unknown>;
  onExport: () => void;
}

export function WorkspaceHeader({ name, dirtyCount, closing, onClose, onExport }: WorkspaceHeaderProps) {
  return (
    <header className="bar">
      <Link to="/" className="bar__brand">
        <span className="bar__mark" aria-hidden="true" />
        Savebench
      </Link>
      <span className="bar__workspace">{name}</span>
      <span className="bar__spacer" />
      <div className="bar__actions">
        <button
          type="button"
          className="btn btn--quiet btn--small"
          disabled={closing}
          onClick={() => void onClose()}
        >
          {closing ? "Closing…" : "Close dump"}
        </button>
        <button type="button" className="btn btn--primary" onClick={onExport}>
          Export
          {dirtyCount > 0 && <span className="btn__count" aria-label={`${dirtyCount} edited files`}>{dirtyCount}</span>}
        </button>
      </div>
    </header>
  );
}
