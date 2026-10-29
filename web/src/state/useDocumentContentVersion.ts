import { useCallback, useSyncExternalStore } from "react";
import type { DocumentStore } from "./documentStore";

/** Read caches follow content and revision changes, without refetching for validation messages. */
export function useDocumentContentVersion(store: DocumentStore) {
  const subscribe = useCallback((listener: () => void) => store.subscribe(null, listener), [store]);
  const version = useSyncExternalStore(subscribe, () => store.valueVersion);
  const revision = useSyncExternalStore(subscribe, () => store.state.revision);
  return { version, revision };
}
