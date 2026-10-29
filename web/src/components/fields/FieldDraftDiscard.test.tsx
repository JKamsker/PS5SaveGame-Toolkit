import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import type { EditResult, FieldNode, FieldValue } from "../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { deferred } from "../../test/deferred";
import { TableCell } from "../document/table/TableCell";
import { FieldRow } from "./FieldRow";

afterEach(cleanup);
const integer = { kind: "integer" as const, min: "0", max: "99" };
const scalar: FieldNode = { type: "field", valueId: "quantity", label: "Quantity", value: "2", spec: integer };
const variants: FieldNode[] = [scalar, { ...scalar, spec: { kind: "nullable", inner: { kind: "text" } } },
  { ...scalar, spec: { kind: "union", options: [{ id: "text", label: "Text", spec: { kind: "text" } }] },
    value: { kind: "union", type: "text", value: "2" } }];

function setup(node = scalar, compact = false, detailed = true) {
  const state = { canUndo: true, canRedo: false, isDirty: true, revision: 1 };
  const store = new DocumentStore(state);
  const rejection = (message = "Quantity rejected"): EditResult => ({ ok: false, changes: [], warnings: ["Keep this warning"],
    errors: [{ valueId: "quantity", message }], structureChanged: false, state });
  const submit = vi.fn<(value: FieldValue) => Promise<EditResult>>(async () => rejection());
  const commit = vi.fn(async (id: string, value: FieldValue) => {
    const result = await submit(value);
    const diagnostics = store.applyWithDiagnostics(result);
    return { accepted: result.ok, rejection: result.ok ? undefined : diagnostics.get(id) };
  });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", canEdit: true, structureVersion: 0, store,
    commit: async (id, value) => (await commit(id, value)).accepted, commitWithDiagnostics: detailed ? commit : undefined,
    mutate: call => call(), acceptResult: vi.fn(), runAction: vi.fn(), refresh: vi.fn() };
  render(<DocumentContext.Provider value={context}>{compact
    ? <TableCell node={node} rowLabel="Potion" onOpenDetails={() => {}} /> : <FieldRow node={node} />}</DocumentContext.Provider>);
  const input = screen.getByRole<HTMLInputElement>("textbox", { name: compact ? "Quantity (Potion)" : "Quantity" });
  return { store, submit, commit, input, rejection, state };
}

async function enter(input: HTMLInputElement, value: string) {
  await act(async () => {
    fireEvent.change(input, { target: { value } });
    fireEvent.keyDown(input, { key: "Enter" });
  });
}

it.each(variants.flatMap(node => [false, true].map(compact => ({ node, compact }))))(
  "dismisses only the settled submission diagnostic when Escape restores $node.spec.kind (compact=$compact)", async ({ node, compact }) => {
    const { store, commit, input, state } = setup(node, compact);
    act(() => { store.setError("other", "Other field error"); store.setError("", "Global error"); });
    await enter(input, "9");
    expect(input.value).toBe("9");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("textbox", { description: /Quantity rejected/ })).toBe(input);
    fireEvent.keyDown(input, { key: "Escape" });
    expect(input.value).toBe("2");
    expect(input.getAttribute("aria-invalid")).toBe("false");
    expect(store.error("quantity")).toBeUndefined();
    expect(screen.queryByText("Quantity rejected")).toBeNull();
    expect(store.error("other")).toBe("Other field error");
    expect(store.error("")).toBe("Global error");
    expect(store.lastMessage).toBe("Keep this warning");
    expect(store.state).toEqual(state);
    expect(store.isModified("quantity")).toBe(false);
    await act(async () => { fireEvent.keyDown(input, { key: "Enter" }); fireEvent.blur(input); });
    expect(commit).toHaveBeenCalledExactlyOnceWith("quantity", node.spec.kind === "union" ? { kind: "union", type: "text", value: "9" } : "9");
  },
);

it("keeps the diagnostic during a pending retry and announces its fresh rejection after Escape", async () => {
  const { input, submit, store, rejection } = setup();
  await enter(input, "9");
  const pending = deferred<EditResult>();
  submit.mockReturnValueOnce(pending.promise);
  await enter(input, "8");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("2");
  expect(store.error("quantity")).toBe("Quantity rejected");
  expect(input.getAttribute("aria-invalid")).toBe("true");
  await act(async () => { pending.resolve(rejection("Fresh rejection")); await pending.promise; });
  expect(input.value).toBe("2");
  expect(screen.getByRole("alert").textContent).toBe("Fresh rejection");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(store.error("quantity")).toBe("Fresh rejection");
  expect(input.getAttribute("aria-invalid")).toBe("true");
  expect(submit).toHaveBeenCalledTimes(2);
});

it("preserves newer typing when an older rejection arrives, then permits a new submission", async () => {
  const { input, submit, rejection, state, store } = setup();
  const pending = deferred<EditResult>();
  submit.mockReturnValueOnce(pending.promise);
  await enter(input, "9");
  fireEvent.change(input, { target: { value: "8" } });
  await act(async () => { pending.resolve(rejection()); await pending.promise; });
  expect(input.value).toBe("8");
  expect(input.getAttribute("aria-invalid")).toBe("true");
  submit.mockResolvedValueOnce({ ok: true, errors: [], warnings: [], structureChanged: false,
    changes: [{ valueId: "quantity", oldValue: "2", newValue: "8" }], state: { ...state, revision: 2 } });
  await act(async () => { fireEvent.keyDown(input, { key: "Enter" }); });
  expect(input.value).toBe("8");
  expect(input.getAttribute("aria-invalid")).toBe("false");
  expect(store.value("quantity", null)).toBe("8");
  expect(submit.mock.calls).toEqual([["9"], ["8"]]);
});

it("does not dismiss a newer field diagnostic even when its text matches the settled rejection", async () => {
  const { input, store } = setup();
  await enter(input, "9");
  act(() => store.setError("quantity", "Quantity rejected"));
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("2");
  expect(store.error("quantity")).toBe("Quantity rejected");
  expect(input.getAttribute("aria-invalid")).toBe("true");
});

it("still accepts an in-flight edit after Escape and restores its saved value on a later discard", async () => {
  const { input, submit, store, state } = setup();
  const pending = deferred<EditResult>();
  submit.mockReturnValueOnce(pending.promise);
  await enter(input, "9");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("2");
  await act(async () => {
    pending.resolve({ ok: true, errors: [], warnings: [], structureChanged: false,
      changes: [{ valueId: "quantity", oldValue: "2", newValue: "9" }], state: { ...state, revision: 2 } });
    await pending.promise;
  });
  expect(input.value).toBe("9");
  expect(store.value("quantity", null)).toBe("9");
  fireEvent.change(input, { target: { value: "8" } });
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("9");
  expect(store.isModified("quantity")).toBe(true);
  expect(submit).toHaveBeenCalledExactlyOnceWith("9");
});

it("does not treat an unchanged value or an unrelated diagnostic as a rejected draft", () => {
  const { input, store, commit } = setup();
  act(() => store.setError("quantity", "Current saved value diagnostic"));
  fireEvent.keyDown(input, { key: "Escape" });
  fireEvent.change(input, { target: { value: "7" } });
  fireEvent.keyDown(input, { key: "Escape" });
  expect(store.error("quantity")).toBe("Current saved value diagnostic");
  expect(commit).not.toHaveBeenCalled();
});

it("keeps a diagnostic when a boolean-only commit cannot prove submission ownership", async () => {
  const { input, store } = setup(scalar, false, false);
  await enter(input, "9");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("2");
  expect(store.error("quantity")).toBe("Quantity rejected");
  expect(input.getAttribute("aria-invalid")).toBe("true");
});
