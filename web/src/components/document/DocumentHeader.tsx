import type { Ref } from "react";
import { api } from "../../api/client";
import type { SaveDocument, SaveFileInfo } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { HistoryButtons } from "./HistoryButtons";

const maturityText = { ReadOnly: "read-only", Experimental: "experimental", Stable: "tested in-game" } as const;

interface DocumentHeaderProps {
  document: SaveDocument;
  file: SaveFileInfo | undefined;
  focusHeading: Ref<HTMLHeadingElement>;
  onSwitchPlugin: (pluginId: string) => void;
}

export function DocumentHeader({ document, file, focusHeading, onSwitchPlugin }: DocumentHeaderProps) {
  const context = useDocument();
  const { workspaceId, documentId } = context;
  return (
        <header className="editor__head">
          <div>
            <h1 ref={focusHeading} tabIndex={-1} className="editor__title">{document.title}</h1>
            <span className="editor__file mono">{document.filePath}</span>
          </div>
          <HistoryButtons store={context.store} onUndo={() => context.mutate(() => api.undo(workspaceId, documentId))} onRedo={() => context.mutate(() => api.redo(workspaceId, documentId))} />
          {!document.groupMembers && <a className="btn btn--small btn--quiet" href={api.downloadUrl(workspaceId, documentId)} download>
            Download file
          </a>}
          <div className="editor__plugin">
            <span>
              {document.plugin.name} <span className={`tag tag--${document.plugin.maturity.toLowerCase()}`}>{maturityText[document.plugin.maturity]}</span>
            </span>
            {!document.groupMembers && file && file.matches.length > 1 && (
              <label>
                <span className="visually-hidden">Open with</span>
                <select value={document.plugin.id} onChange={(e) => onSwitchPlugin(e.target.value)}>
                  {file.matches.map((m) => (
                    <option key={m.pluginId} value={m.pluginId}>
                      Open with {m.pluginName}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
        </header>
  );
}
