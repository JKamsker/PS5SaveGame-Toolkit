import type { EditResult } from "../../api/protocol";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { actionReview } from "../../api/actionReview";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "./ActionButton";

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function setup(planned: boolean) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const runAction = vi.fn(async () => ({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false, state: store.state }));
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ revision: 1, scope: {},
    plan: { changes: [], skippedRows: [], errors: [], warnings: [] } });
  const context = { workspaceId: "w", documentId: "d", canEdit: true, store, structureVersion: 0,
    commit: vi.fn(async () => true), mutate: async (call: () => Promise<EditResult>) => call(), acceptResult: async () => {}, runAction, refresh: async () => {} };
  render(<DocumentContext.Provider value={context}><ActionButton node={{ type: "action", actionId: "run", label: "Run",
    supportsPlanning: planned, parameters: [{ type: "field", valueId: "index", label: "Entry index",
      spec: { kind: "integer", min: "0", max: "3" }, value: "0" }] }} /></DocumentContext.Provider>);
  fireEvent.click(screen.getByRole("button", { name: "Run" }));
  const dialog = screen.getByRole("dialog");
  return { runAction, plan, dialog: within(dialog), input: within(dialog).getByRole("textbox", { name: "Entry index" }),
    submit: within(dialog).getByRole("button", { name: planned ? "Preview" : "Run" }) as HTMLButtonElement };
}

describe.each([false, true])("action parameter validation (planned: %s)", planned => {
  it("keeps an invalid draft visible and blocks submitting an older value", () => {
    const { input, submit, runAction, plan, dialog } = setup(planned);
    fireEvent.change(input, { target: { value: "99" } });
    fireEvent.blur(input);
    const error = dialog.getByRole("alert");
    expect(error.textContent).toBe("Must be between 0 and 3.");
    expect(input.getAttribute("aria-describedby")?.split(" ")).toContain(error.id);
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(submit.disabled).toBe(true);
    // A form submission must also respect validation, independently of its disabled button.
    fireEvent.submit(submit.closest("form")!);
    expect(runAction).not.toHaveBeenCalled();
    expect(plan).not.toHaveBeenCalled();
    expect(input).toHaveProperty("value", "99");
  });

  it("consumes Enter to validate the field without implicitly submitting the form", () => {
    const { input, runAction, plan } = setup(planned);
    fireEvent.change(input, { target: { value: "99" } });
    // jsdom does not implement implicit Enter submission: cancellation is the browser contract.
    expect(fireEvent.keyDown(input, { key: "Enter" })).toBe(false);
    expect(screen.getByRole("alert").textContent).toBeTruthy();
    expect(runAction).not.toHaveBeenCalled();
    expect(plan).not.toHaveBeenCalled();
  });

  it("allows explicit submission with the corrected parameter", () => {
    const { input, submit, runAction, plan } = setup(planned);
    fireEvent.change(input, { target: { value: "99" } });
    fireEvent.blur(input);
    fireEvent.change(input, { target: { value: "2" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(submit.disabled).toBe(false);
    expect(runAction).not.toHaveBeenCalled();
    expect(plan).not.toHaveBeenCalled();
    fireEvent.click(submit);
    if (planned) expect(plan).toHaveBeenCalledWith("w", "d", "run", { index: "2" }, {}, 1);
    else expect(runAction).toHaveBeenCalledWith("run", { index: "2" });
  });

  if (planned) it("clears local errors when reopening with fresh default parameters", () => {
    const { input, plan } = setup(planned);
    fireEvent.change(input, { target: { value: "99" } });
    fireEvent.blur(input);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.click(screen.getByRole("button", { name: "Run" }));
    expect(screen.getByRole("textbox", { name: "Entry index" })).toHaveProperty("value", "0");
    const reopened = within(screen.getByRole("dialog"));
    const submit = reopened.getByRole("button", { name: "Preview" });
    expect(submit).toHaveProperty("disabled", false);
    expect(screen.queryByRole("alert")).toBeNull();
    fireEvent.click(submit);
    expect(plan).toHaveBeenCalledExactlyOnceWith("w", "d", "run", { index: "0" }, {}, 1);
  });
});
