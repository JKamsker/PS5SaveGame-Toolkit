import { render } from "@testing-library/react";
import { vi } from "vitest";
import type { EditResult, FieldNode, FieldSpec, FieldValue, TableNode, TableRow, ViewNode } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { TableView } from "../TableView";

/** Gives the virtualiser a viewport in jsdom (which has no layout). */
export function stubViewport(height = 600, width = 1000): () => void {
  const proto = HTMLElement.prototype;
  const original = {
    clientHeight: Object.getOwnPropertyDescriptor(proto, "clientHeight"),
    clientWidth: Object.getOwnPropertyDescriptor(proto, "clientWidth"),
  };
  Object.defineProperty(proto, "clientHeight", { configurable: true, get: () => height });
  Object.defineProperty(proto, "clientWidth", { configurable: true, get: () => width });
  return () => {
    if (original.clientHeight) Object.defineProperty(proto, "clientHeight", original.clientHeight);
    if (original.clientWidth) Object.defineProperty(proto, "clientWidth", original.clientWidth);
  };
}

export function field(valueId: string, spec: FieldSpec, value: FieldValue, extra: Partial<FieldNode> = {}): FieldNode {
  return { type: "field", valueId, label: valueId, spec, value, ...extra };
}

export function text(value: string): ViewNode {
  return { type: "text", text: value };
}

export function table(rows: TableRow[], columns = ["name", "value"], title = "Items"): TableNode {
  return { type: "table", title, columns: columns.map((key) => ({ key, label: key[0]!.toUpperCase() + key.slice(1) })), rows };
}

/** Renders a table inside a document context whose commit applies edits to the store, like the server would. */
export function renderTable(node: TableNode, options: { canEdit?: boolean } = {}) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false });
  const commit = vi.fn(async (valueId: string, value: FieldValue) => {
    const result: EditResult = {
      ok: true,
      errors: [],
      changes: [{ valueId, oldValue: store.value(valueId, null), newValue: value }],
      warnings: [],
      structureChanged: false,
      state: { ...store.state, isDirty: true, canUndo: true },
    };
    store.apply(result);
    return true;
  });
  const context: DocumentContextValue = {
    workspaceId: "w1",
    documentId: "d1",
    canEdit: options.canEdit ?? true,
    store,
    structureVersion: 0,
    commit,
    runAction: vi.fn(async () => ({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false, state: store.state })),
    refresh: vi.fn(async () => {}),
  };
  const utils = render(
    <DocumentContext.Provider value={context}>
      <TableView node={node} />
    </DocumentContext.Provider>,
  );
  return { ...utils, store, commit };
}
