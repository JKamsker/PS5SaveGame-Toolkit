import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { ReferenceRecordsResponse } from "../../../api/protocol";
import { createDocumentFixture } from "../../../test/documentFixture";
import { deferred } from "../../../test/deferred";
import { DocumentContext } from "../../../state/documentStore";
import { ReferenceControl } from "./ReferenceControl";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const pair = { fieldId: "item", recordKey: "1001" };
const candidate = { rawId: "1001", label: "Potion", eligibility: { available: true, eligible: true, selectable: true, reasons: [] } };
const found: ReferenceRecordsResponse = { revision: 0, results: [{ ...pair, status: 200, candidate }] };

function setup(value = "1001", compact = false) {
  const fixture = createDocumentFixture({ state: { canUndo: false, canRedo: false, isDirty: false, revision: 0 } });
  const field = (rawId: string) => <DocumentContext.Provider value={fixture.context}>
    <ReferenceControl id="item-control" valueId="item" value={rawId} compact={compact} spec={{ kind: "reference", catalogId: "items" }} onCommit={value => fixture.context.commit("item", value)} />
  </DocumentContext.Provider>;
  const view = render(field(value));
  return { ...view, ...fixture, rerenderValue: (rawId: string) => view.rerender(field(rawId)) };
}

it.each([
  new ApiError("Invalid label request", 400),
  new ApiError("Session expired. Import the save again.", 404),
  new ApiError("Label service unavailable", 503),
  new ApiError("Request failed (502).", 502),
  new TypeError("Failed to fetch"),
])("shows a failed batch read and retries without changing the saved ID (%s)", async failure => {
  const lookup = vi.spyOn(api, "referenceRecords").mockRejectedValueOnce(failure).mockResolvedValue(found);
  const { context, store } = setup();
  expect((await screen.findByRole("alert")).textContent).toContain(failure.message);
  const trigger = screen.getByRole("button", { name: "1001" });
  expect(trigger.getAttribute("aria-describedby")).toContain("item-control-lookup-error");
  const retry = screen.getByRole("button", { name: "Retry reference label" });
  retry.focus();
  fireEvent.click(retry);
  expect(document.activeElement).toBe(trigger);
  await screen.findByRole("button", { name: "Potion (1001)" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(lookup).toHaveBeenCalledTimes(2);
  expect(lookup).toHaveBeenLastCalledWith("w1", "d1", [pair], 0);
  expect(context.commit).not.toHaveBeenCalled();
  expect(store.state).toEqual({ canUndo: false, canRedo: false, isDirty: false, revision: 0 });
});

it("reports a per-pair failure and retains the existing field descriptions", async () => {
  vi.spyOn(api, "referenceRecords").mockResolvedValue({ revision: 0, results: [{ ...pair, status: 404, error: "Unknown reference field." }] });
  const fixture = createDocumentFixture();
  render(<DocumentContext.Provider value={fixture.context}>
    <span id="help">Choose an item.</span>
    <ReferenceControl id="item-control" valueId="item" value="1001" describedBy="help"
      spec={{ kind: "reference", catalogId: "items" }} onCommit={value => fixture.context.commit("item", value)} />
  </DocumentContext.Provider>);
  expect((await screen.findByRole("alert")).textContent).toContain("Unknown reference field.");
  expect(screen.getByRole("button", { name: "1001" }).getAttribute("aria-describedby")).toBe("help item-control-lookup-error");
});

it("uses a short compact retry while preserving the full error announcement and description", async () => {
  const message = "Reference service unavailable. Try again when the backend reconnects.";
  vi.spyOn(api, "referenceRecords").mockRejectedValueOnce(new ApiError(message, 503)).mockResolvedValue(found);
  setup("1001", true);
  expect((await screen.findByRole("alert")).textContent).toContain(message);
  const retry = screen.getByRole("button", { name: "Retry reference label" });
  expect(retry.textContent).toBe("! Retry");
  expect(retry.getAttribute("title")).toBe(message);
  const trigger = screen.getByRole("button", { name: "1001" });
  expect(trigger.getAttribute("aria-describedby")).toBe("item-control-lookup-error");
  retry.focus();
  fireEvent.click(retry);
  expect(document.activeElement).toBe(trigger);
  await screen.findByRole("button", { name: "Potion (1001)" });
  expect(screen.queryByRole("alert")).toBeNull();
});

it("ignores obsolete failures after the saved ID changes", async () => {
  const older = deferred<ReferenceRecordsResponse>();
  const lookup = vi.spyOn(api, "referenceRecords").mockReturnValueOnce(older.promise).mockResolvedValue({ ...found,
    results: [{ ...pair, status: 200, recordKey: "2002", candidate: { ...candidate, rawId: "2002", label: "Ether" } }],
  });
  const { rerenderValue } = setup();
  await waitFor(() => expect(lookup).toHaveBeenCalledTimes(1));
  rerenderValue("2002");
  await screen.findByRole("button", { name: "Ether (2002)" });
  await act(async () => { older.reject(new Error("Obsolete failure")); await older.promise.catch(() => {}); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("button", { name: "Ether (2002)" })).toBeTruthy();
});

it.each(["resolve", "reject"] as const)("retires a pending batch on unmount (%s)", async outcome => {
  const pending = deferred<ReferenceRecordsResponse>();
  const lookup = vi.spyOn(api, "referenceRecords").mockReturnValue(pending.promise);
  const { unmount, context } = setup();
  await waitFor(() => expect(lookup).toHaveBeenCalledTimes(1));
  unmount();
  await act(async () => {
    if (outcome === "resolve") pending.resolve(found);
    else pending.reject(new Error("Obsolete failure"));
    await pending.promise.catch(() => {});
  });
  expect(context.commit).not.toHaveBeenCalled();
  expect(context.refresh).not.toHaveBeenCalled();
  expect(screen.queryByRole("alert")).toBeNull();
});
