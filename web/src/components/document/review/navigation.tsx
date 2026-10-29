import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { ViewTarget } from "../../../api/spatial";
import type { CollectionQuery } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import { useDocumentContentVersion } from "../../../state/useDocumentContentVersion";

interface JumpTarget {
  valueId?: string; actionId?: string; rowId?: string; path: readonly string[];
  collection?: { collectionId: string; rowKey: string; query: CollectionQuery };
}
interface Navigation { target: JumpTarget | null; jump: (change: JumpTarget) => void; clear: () => void }
const ReviewNavigation = createContext<Navigation>({ target: null, jump: () => {}, clear: () => {} });
const ReviewPathContext = createContext<readonly string[]>([]);

export function ReviewNavigationProvider({ children }: { children: ReactNode }) {
  const doc = useDocument();
  const { revision, version } = useDocumentContentVersion(doc.store);
  const binding = useMemo(() => ({ store: doc.store, revision, version, structureVersion: doc.structureVersion }),
    [doc.store, revision, version, doc.structureVersion]);
  const [requested, setRequested] = useState<{ target: JumpTarget; binding: typeof binding } | null>(null);
  const target = requested?.binding === binding ? requested.target : null;
  const jump = useCallback((change: JumpTarget) => setRequested({ target: { ...change }, binding }), [binding]);
  const clear = useCallback(() => setRequested(null), []);
  const navigation = useMemo(() => ({ target, jump, clear }), [target, jump, clear]);
  return <ReviewNavigation.Provider value={navigation}>{children}</ReviewNavigation.Provider>;
}

export const useReviewNavigation = () => useContext(ReviewNavigation);

/** Consume the route one container at a time: nested tabs may reuse the same titles. */
export function ReviewPath({ part, children }: { part: string; children: ReactNode }) {
  const parent = useContext(ReviewPathContext);
  const path = useMemo(() => [...parent, part], [parent, part]);
  return <ReviewPathContext.Provider value={path}>{children}</ReviewPathContext.Provider>;
}

export function useNextReviewPath() {
  const path = useContext(ReviewPathContext);
  const { target } = useReviewNavigation();
  return target && path.every((part, index) => target.path[index] === part) ? target.path[path.length] : undefined;
}

/** Also runs when a lazy/virtualised field mounts after a jump opened its parents. */
export function useFieldJump(valueId: string) {
  return useViewJump(valueId, "Field");
}

export function useViewJump(id: string, kind: ViewTarget["kind"]) {
  const { target } = useReviewNavigation();
  const path = useContext(ReviewPathContext);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const targetId = kind === "Field" ? target?.valueId : kind === "Action" ? target?.actionId : target?.rowId;
    if (targetId !== id || !ref.current) return;
    // An F8 identity can also occur in another rendered projection of the save.
    if (target?.collection && (target.path.length !== path.length || !path.every((part, index) => target.path[index] === part))) return;
    if (kind === "Row" && (target!.path.length !== path.length + 1 || !path.every((part, index) => target!.path[index] === part))) return;
    const element = ref.current;
    element.scrollIntoView?.({ block: "center" });
    // Prefer a field's value editor over a surrounding union-type selector.
    const control = (kind === "Field" ? element.querySelector<HTMLElement>("input:not(:disabled), textarea:not(:disabled)") : null)
      ?? element.querySelector<HTMLElement>("input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)");
    (control ?? element).focus({ preventScroll: true });
  }, [target, id, kind, path]);
  return ref;
}

export function useSpatialJump() {
  const { jump } = useReviewNavigation();
  return (target: ViewTarget) => jump({
    valueId: target.kind === "Field" ? target.id : undefined,
    actionId: target.kind === "Action" ? target.id : undefined,
    rowId: target.kind === "Row" ? target.id : undefined,
    path: target.kind === "Row" ? [...(target.path ?? []), `row:${target.id}`] : target.path ?? [],
  });
}
