import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "./client";
import { queueReferenceRecord } from "./referenceBatch";

beforeEach(() => {
  vi.useFakeTimers();
  vi.spyOn(api, "referenceRecords").mockImplementation(async (_ws, _doc, pairs, revision) => ({ revision: revision!,
    results: pairs.map(pair => ({ ...pair, status: 200, candidate: { rawId: pair.recordKey, label: pair.fieldId,
      eligibility: { available: true, eligible: true, selectable: true, reasons: [] } } })) }));
});
afterEach(() => { vi.restoreAllMocks(); vi.useRealTimers(); });

const pair = { fieldId: "field", recordKey: "same-key" };
const queue = (revision = 0, structure = 0) => queueReferenceRecord("w", "d", revision, structure, pair);

it("splits oversized renders into bounded chunks without dropping duplicates", async () => {
  const lookups = Array.from({ length: 257 }, () => queue());
  await vi.advanceTimersByTimeAsync(30);
  expect(vi.mocked(api.referenceRecords).mock.calls.map(call => call[2].length)).toEqual([256, 1]);
  const results = await Promise.all(lookups.map(lookup => lookup.result));
  expect(results).toHaveLength(257);
  expect(results.every(result => result?.label === "field")).toBe(true);
});

it("isolates revisions and rebuilt bindings even when the raw key matches", async () => {
  const lookups = [queue(), queue(1), queue(1, 1)];
  await vi.advanceTimersByTimeAsync(30);
  await Promise.all(lookups.map(lookup => lookup.result));
  expect(api.referenceRecords).toHaveBeenCalledTimes(3);
});

it("removes cancelled cells before sending and tolerates StrictMode remounts", async () => {
  const cancelled = queue();
  cancelled.cancel();
  const live = queue();
  await vi.advanceTimersByTimeAsync(30);
  expect(await cancelled.result).toBeUndefined();
  expect((await live.result)?.label).toBe("field");
  expect(api.referenceRecords).toHaveBeenCalledExactlyOnceWith("w", "d", [pair], 0);
});

it("returns per-pair errors without losing successful labels", async () => {
  vi.mocked(api.referenceRecords).mockResolvedValue({ revision: 0, results: [
    { ...pair, status: 404, error: "Unknown reference field." },
    { ...pair, status: 200, candidate: { rawId: pair.recordKey, eligibility: { available: true, eligible: false, selectable: false, reasons: ["Blocked"] } } },
  ] });
  const missing = queue();
  const failed = expect(missing.result).rejects.toMatchObject({ status: 404 });
  const valid = queue();
  await vi.advanceTimersByTimeAsync(30);
  await failed;
  expect((await valid.result)?.eligibility.selectable).toBe(false);
});

it("fans out whole-request stale failures for bounded recovery in the cells", async () => {
  vi.mocked(api.referenceRecords).mockRejectedValue(new ApiError("Stale", 409, { code: "stale" }));
  const failed = [queue(), queue()].map(lookup => expect(lookup.result).rejects.toMatchObject({ status: 409, body: { code: "stale" } }));
  await vi.advanceTimersByTimeAsync(30);
  await Promise.all(failed);
});

it("rejects mismatched identities instead of assigning another field's label", async () => {
  vi.mocked(api.referenceRecords).mockResolvedValue({ revision: 0, results: [{ ...pair, fieldId: "another-field", status: 404, error: "Wrong field" }] });
  const failed = expect(queue().result).rejects.toThrow("field identities");
  await vi.advanceTimersByTimeAsync(30);
  await failed;
});
