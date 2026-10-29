import { QueryClient } from "@tanstack/react-query";
import { expect, it, vi } from "vitest";
import { deferred } from "../../test/deferred";
import { documentClosures } from "./documentClosures";

it("keeps a synchronous close guard by client/workspace/document until its request settles", async () => {
  const client = new QueryClient();
  const closures = documentClosures(client);
  const gate = deferred<void>();
  const validate = vi.fn();
  const close = vi.fn(() => gate.promise);
  const first = closures.run("w", "a", validate, close);
  // Another editor for the same identity gets exactly the same pending operation owner.
  expect(documentClosures(client).get("w", "a").pending).toBe(true);
  await documentClosures(client).run("w", "a", validate, close);
  expect(validate).toHaveBeenCalledOnce(); expect(close).toHaveBeenCalledOnce();
  expect(closures.get("w", "b").pending).toBe(false);
  expect(closures.get("other-workspace", "a").pending).toBe(false);
  expect(documentClosures(new QueryClient()).get("w", "a").pending).toBe(false);
  gate.resolve(); await first;
  expect(closures.get("w", "a")).toEqual({ pending: false, error: null });
});

it("publishes rejection for a remounted editor and releases the guard for explicit retry", async () => {
  const closures = documentClosures(new QueryClient());
  const gate = deferred<void>();
  const first = closures.run("w", "a", () => {}, () => gate.promise);
  gate.reject(new Error("Close failed.")); await first;
  expect(closures.get("w", "a")).toEqual({ pending: false, error: "Close failed." });
  const retry = vi.fn(async () => {});
  await closures.run("w", "a", () => {}, retry);
  expect(retry).toHaveBeenCalledOnce();
  expect(closures.get("w", "a")).toEqual({ pending: false, error: null });
});

it("does not publish a pending close or execute DELETE when live validation refuses it", async () => {
  const closures = documentClosures(new QueryClient());
  const changed = vi.fn(); closures.subscribe(changed);
  const close = vi.fn(async () => {});
  await closures.run("w", "a", () => { throw new Error("Pending edits."); }, close);
  expect(close).not.toHaveBeenCalled();
  expect(changed).toHaveBeenCalledOnce(); // Only the diagnostic, never a transient closing view.
  expect(closures.get("w", "a")).toEqual({ pending: false, error: "Pending edits." });
});
