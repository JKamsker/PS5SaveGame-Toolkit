import { expect, it, vi } from "vitest";
import type { EditResult } from "../api/protocol";
import { createDocumentFixture, createQueryClientScope } from "./documentFixture";

const result: EditResult = { ok: true, errors: [], warnings: [], structureChanged: false,
  changes: [{ valueId: "a", oldValue: false, newValue: true }],
  state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };

it.each([false, true])("keeps mutation acceptance and store application explicit (apply=%s)", applyResults => {
  const { context, store } = createDocumentFixture({ acceptMutations: true, applyResults });
  return context.mutate(async () => result).then(outcome => {
    expect(outcome).toBe(result);
    expect(context.acceptResult).toHaveBeenCalledExactlyOnceWith(result);
    expect(store.value("a", false)).toBe(applyResults);
  });
});

it("can leave result acceptance entirely to a test's caller", async () => {
  const acceptResult = vi.fn();
  const { context } = createDocumentFixture({ context: { acceptResult } });
  await context.mutate(async () => result);
  expect(acceptResult).not.toHaveBeenCalled();
});

it("clears registered query caches while preserving caller-supplied query options", () => {
  const scope = createQueryClientScope();
  const client = scope.create({ defaultOptions: { queries: { gcTime: Infinity, retry: false } } });
  client.setQueryData(["fixture"], "cached");
  expect(client.getDefaultOptions().queries).toEqual({ gcTime: Infinity, retry: false });
  scope.clear();
  expect(client.getQueryData(["fixture"])).toBeUndefined();
});
