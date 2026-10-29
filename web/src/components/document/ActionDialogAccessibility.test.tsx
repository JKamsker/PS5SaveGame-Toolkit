import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../api/actionReview";
import type { ActionNode, EditResult } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "./ActionButton";

const parameter = { type: "field" as const, valueId: "count", label: "Target count", value: "1",
  spec: { kind: "integer" as const, min: "0", max: "99" } };
const preview: ActionPreview = { revision: 1, planId: "plan", scope: {}, plan: {
  changes: [], skippedRows: [], errors: [], warnings: [],
} };
const result: EditResult = { ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
  state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };

beforeEach(() => {
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value: function (this: HTMLDialogElement) {
    this.open = false;
    this.dispatchEvent(new Event("close"));
  } });
});
afterEach(() => {
  cleanup(); vi.restoreAllMocks();
  Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal");
  Reflect.deleteProperty(HTMLDialogElement.prototype, "close");
});

function setup(supportsPlanning: boolean) {
  const runAction = vi.fn(async () => result);
  const node: ActionNode = { type: "action", actionId: "refill", label: "Refill", parameters: [parameter], supportsPlanning };
  render(<DocumentContext.Provider value={{ workspaceId: "w", documentId: "d", canEdit: true,
    store: new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 }), structureVersion: 0,
    refresh: vi.fn(), acceptResult: vi.fn(), commit: vi.fn(), runAction }}><ActionButton node={node} /></DocumentContext.Provider>);
  const trigger = screen.getByRole("button", { name: "Refill" });
  trigger.focus();
  fireEvent.click(trigger);
  return { trigger, runAction, dialog: screen.getByRole("dialog", { name: supportsPlanning ? "Preview: Refill" : "Refill" }) };
}

it("keeps focus in the action dialog through keyboard editing, planning, parameter return and apply", async () => {
  let finish!: (value: ActionPreview) => void;
  vi.spyOn(actionReview, "plan").mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValue(preview);
  vi.spyOn(actionReview, "apply").mockResolvedValue(result);
  const { trigger, dialog } = setup(true);
  const heading = within(dialog).getByRole("heading", { name: "Preview: Refill" });
  expect(document.activeElement).toBe(heading);
  const input = within(dialog).getByRole("textbox", { name: "Target count" });
  input.focus();
  fireEvent.change(input, { target: { value: "3" } });
  fireEvent.keyDown(input, { key: "Enter" });
  const submit = within(dialog).getByRole("button", { name: "Preview" });
  submit.focus();
  fireEvent.click(submit);
  expect(document.activeElement).toBe(heading);
  await act(async () => { finish(preview); });
  expect(document.activeElement).toBe(heading);
  expect(within(dialog).getByRole("status").textContent).toContain("0 field changes");
  expect(within(dialog).getByRole("region", { name: "Preview changes" }).tabIndex).toBe(0);
  expect(actionReview.plan).toHaveBeenCalledWith("w", "d", "refill", { count: "3" }, {}, 1);
  fireEvent.click(within(dialog).getByRole("button", { name: "Change parameters" }));
  expect(document.activeElement).toBe(within(dialog).getByRole("textbox", { name: "Target count" }));
  fireEvent.click(within(dialog).getByRole("button", { name: "Preview" }));
  fireEvent.click(await within(dialog).findByRole("button", { name: "Apply" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

it("focuses the rejected legacy parameter and restores the opener after success", async () => {
  const { trigger, runAction, dialog } = setup(false);
  expect(document.activeElement).toBe(within(dialog).getByRole("heading", { name: "Refill" }));
  runAction.mockResolvedValueOnce({ ...result, ok: false, errors: [{ valueId: "count", message: "Count rejected" }] });
  fireEvent.click(within(dialog).getByRole("button", { name: "Refill" }));
  await within(dialog).findByRole("alert");
  const input = within(dialog).getByRole("textbox", { name: "Target count", description: "Count rejected" });
  expect(document.activeElement).toBe(input);
  fireEvent.change(input, { target: { value: "2" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Refill" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  await waitFor(() => expect(document.activeElement).toBe(trigger));
});

it("restores the opener after a native close while a plan is pending", async () => {
  let finish!: (value: ActionPreview) => void;
  vi.spyOn(actionReview, "plan").mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const { trigger, dialog } = setup(true);
  fireEvent.click(within(dialog).getByRole("button", { name: "Preview" }));
  // jsdom does not implement Escape's native dialog-closing default action.
  act(() => (dialog as HTMLDialogElement).close());
  await act(async () => { finish(preview); });
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(document.activeElement).toBe(trigger);
});
