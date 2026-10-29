import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import type { EditResult, FieldNode, ViewNode } from "../../api/protocol";
import { DocumentContext } from "../../state/documentStore";
import { createDocumentFixture } from "../../test/documentFixture";
import { deferred } from "../../test/deferred";
import { TableCell } from "./table/TableCell";
import { ViewList } from "./ViewRenderer";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

// Available references arrive normalized by Core, so every projection uses the ordinary editor.
const canonical: FieldNode = { type: "field", valueId: "raw/quantity", label: "Canonical quantity", value: "2",
  spec: { kind: "integer", min: "0", max: "10" }, warning: "Inferred meaning", confidence: "Inferred" };
const projection: FieldNode = { ...canonical, label: "Named quantity" };
const unavailable: ViewNode = { type: "fieldReference", targetValueId: canonical.valueId, label: "Missing quantity", description: "Context remains visible" };
const result = (extra: Partial<EditResult> = {}): EditResult => ({ ok: true, errors: [], changes: [], warnings: [],
  structureChanged: false, state: { canUndo: true, canRedo: false, isDirty: true, revision: 1 }, ...extra });

function setup(canEdit = true, readOnly = false) {
  const fixture = createDocumentFixture({ context: { canEdit } });
  const commit = vi.fn(async (id: string, value: Parameters<typeof fixture.context.commit>[1]) => {
    fixture.store.apply(result({ changes: [{ valueId: id, oldValue: fixture.store.value(id, "2"), newValue: value }] }));
    return true;
  });
  const node = { ...projection, spec: { ...projection.spec, readOnly } };
  const context = { ...fixture.context, commit };
  const ui = (nodes: ViewNode[]) => <DocumentContext.Provider value={context}>
    <ViewList nodes={nodes} />
    <TableCell node={node} rowLabel="Potion" onOpenDetails={() => {}} />
  </DocumentContext.Provider>;
  const mounted = render(ui([canonical, node]));
  return { ...mounted, ...fixture, context, commit, ui };
}

it("gives repeated controls distinct DOM labels while sharing live values, modified state and undo updates", async () => {
  const { commit, store } = setup();
  const first = screen.getByRole<HTMLInputElement>("textbox", { name: /Canonical quantity/ });
  const second = screen.getByRole<HTMLInputElement>("textbox", { name: /Named quantity.*inferred/ });
  const cell = screen.getByRole<HTMLInputElement>("textbox", { name: "Named quantity (Potion)" });
  expect(new Set([first.id, second.id, cell.id]).size).toBe(3);
  second.focus();
  fireEvent.change(second, { target: { value: "7" } });
  await act(async () => { fireEvent.keyDown(second, { key: "Enter" }); });
  expect(commit).toHaveBeenCalledExactlyOnceWith(canonical.valueId, "7");
  expect([first.value, second.value, cell.value]).toEqual(["7", "7", "7"]);
  expect(document.activeElement).toBe(second);
  expect(screen.getAllByText("modified")).toHaveLength(2);
  act(() => store.apply(result({ changes: [{ valueId: canonical.valueId, oldValue: "7", newValue: "2" }],
    state: { canUndo: false, canRedo: true, isDirty: false, revision: 2 } })));
  expect([first.value, second.value, cell.value]).toEqual(["2", "2", "2"]);
  expect(screen.queryByText("modified")).toBeNull();
});

it("keeps shared errors owned by the submitting projection through pending and foreign Escape", async () => {
  const pending = deferred<boolean>();
  const { context, commit, store } = setup();
  commit.mockImplementationOnce(() => pending.promise);
  const first = screen.getByRole<HTMLInputElement>("textbox", { name: /Canonical quantity/ });
  const second = screen.getByRole<HTMLInputElement>("textbox", { name: /Named quantity.*inferred/ });
  await act(async () => {
    fireEvent.change(second, { target: { value: "6" } });
    fireEvent.keyDown(second, { key: "Enter" });
  });
  act(() => store.setError(canonical.valueId, "Six rejected"));
  fireEvent.keyDown(first, { key: "Escape" });
  expect(store.error(canonical.valueId)).toBe("Six rejected");
  fireEvent.keyDown(second, { key: "Escape" });
  expect(store.error(canonical.valueId)).toBe("Six rejected");
  await act(async () => { pending.resolve(false); await pending.promise; });
  expect(first.getAttribute("aria-invalid")).toBe("true");
  expect(second.getAttribute("aria-invalid")).toBe("true");
  expect(context.store.state.isDirty).toBe(false);
});

it.each([[false, false], [true, true]])("preserves document/field read-only policy (canEdit=%s, readOnly=%s)", (canEdit, readOnly) => {
  const { commit } = setup(canEdit, readOnly);
  expect(screen.queryByRole("textbox", { name: /Named quantity/ })).toBeNull();
  expect(commit).not.toHaveBeenCalled();
});

it("renders retired and unloaded targets as useful unavailable text with no edit fallback", () => {
  const { rerender, ui, commit } = setup();
  rerender(ui([unavailable]));
  expect(screen.queryByRole("textbox", { name: /Missing quantity/ })).toBeNull();
  expect(screen.getByText(/Its canonical target may be unloaded, missing, or removed/)).toBeTruthy();
  expect(screen.getByText("Context remains visible")).toBeTruthy();
  expect(commit).not.toHaveBeenCalled();
});

it("renders unavailable table cells with a readable label and never exposes a control", () => {
  const { context } = createDocumentFixture();
  render(<DocumentContext.Provider value={context}><div role="table"><div role="row">
    <TableCell node={unavailable} rowLabel="Potion" onOpenDetails={() => {}} />
  </div></div></DocumentContext.Provider>);
  expect(screen.getByRole("cell").textContent).toBe("Missing quantity: unavailable");
  expect(screen.queryByRole("textbox")).toBeNull();
});
