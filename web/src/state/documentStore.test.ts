import { describe, expect, it, vi } from "vitest";
import { DocumentStore } from "./documentStore";

const state = { canUndo: false, canRedo: false, isDirty: false };

describe("table value subscriptions", () => {
  it("notifies each table once per bulk change, including tables sharing field aliases", () => {
    const store = new DocumentStore(state);
    const inventory = vi.fn();
    const aliases = vi.fn();
    const unrelated = vi.fn();
    store.subscribeValues(["quantity", "enabled"], inventory);
    store.subscribeValues(["quantity"], aliases);
    store.subscribeValues(["other"], unrelated);
    store.apply({ ok: true, errors: [], warnings: [], structureChanged: false, state,
      changes: ["quantity", "enabled"].map(valueId => ({ valueId, oldValue: false, newValue: true })) });
    expect(inventory).toHaveBeenCalledTimes(1);
    expect(aliases).toHaveBeenCalledTimes(1);
    expect(unrelated).not.toHaveBeenCalled();
    expect(store.valueVersion).toBe(1);
  });

  it("releases old subscriptions when a table unmounts and supports subscription replay", () => {
    const store = new DocumentStore(state);
    const listener = vi.fn();
    const unsubscribe = store.subscribeValues(["quantity"], listener);
    unsubscribe();
    store.reset(state);
    expect(listener).not.toHaveBeenCalled();
    const replay = store.subscribeValues(["quantity"], listener);
    store.reset(state);
    expect(listener).toHaveBeenCalledTimes(1);
    replay();
    store.reset(state);
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
