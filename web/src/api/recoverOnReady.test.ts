import { QueryClient, QueryObserver } from "@tanstack/react-query";
import { expect, it, vi } from "vitest";
import type { RuntimeState } from "../runtime/client";
import { refetchFailedWhenReady } from "./recoverOnReady";

it("fetches reads that failed while the runtime was loading again once it is ready", async () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let state: RuntimeState = { status: "loading", loaded: 1, total: 10 };
  const listeners = new Set<() => void>();
  const emit = (next: RuntimeState) => { state = next; listeners.forEach(listener => listener()); };
  const stop = refetchFailedWhenReady(queryClient, { state: () => state, subscribe: listener => { listeners.add(listener); return () => listeners.delete(listener); } });

  // Mounted reads: one failed while waiting for the boot, one succeeded.
  const read = vi.fn().mockRejectedValueOnce(new Error("The editor runtime stopped loading.")).mockResolvedValue(["plugin"]);
  const healthy = vi.fn().mockResolvedValue("ok");
  const failed = new QueryObserver(queryClient, { queryKey: ["plugins"], queryFn: read });
  const fine = new QueryObserver(queryClient, { queryKey: ["healthy"], queryFn: healthy });
  const unsubscribe = [failed.subscribe(() => undefined), fine.subscribe(() => undefined)];
  await vi.waitFor(() => expect(failed.getCurrentResult().status).toBe("error"));
  await vi.waitFor(() => expect(fine.getCurrentResult().status).toBe("success"));

  emit({ status: "loading", loaded: 2, total: 10 });
  expect(read).toHaveBeenCalledTimes(1);
  emit({ status: "ready", boot: {} as never });
  await vi.waitFor(() => expect(failed.getCurrentResult().data).toEqual(["plugin"]));
  expect(healthy).toHaveBeenCalledTimes(1);
  stop();
  unsubscribe.forEach(done => done());
});
