import type { QueryClient } from "@tanstack/react-query";
import type { RuntimeClient } from "../runtime/client";

/**
 * Reads that failed while the runtime was still loading (a boot that reported no progress for a while)
 * are fetched again once it is ready, so the page recovers without a reload.
 */
export function refetchFailedWhenReady(queryClient: QueryClient, runtime: Pick<RuntimeClient, "state" | "subscribe">): () => void {
  let ready = runtime.state().status === "ready";
  return runtime.subscribe(() => {
    const now = runtime.state().status === "ready";
    if (now && !ready) void queryClient.invalidateQueries({ predicate: query => query.state.status === "error" });
    ready = now;
  });
}
