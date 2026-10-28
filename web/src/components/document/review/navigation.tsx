import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { ViewTarget } from "../../../api/spatial";

interface JumpTarget { valueId?: string; actionId?: string; rowId?: string; path: readonly string[] }
interface Navigation { target: JumpTarget | null; jump: (change: JumpTarget) => void }
const ReviewNavigation = createContext<Navigation>({ target: null, jump: () => {} });
const ReviewPathContext = createContext<readonly string[]>([]);

export function ReviewNavigationProvider({ children }: { children: ReactNode }) {
  const [target, setTarget] = useState<JumpTarget | null>(null);
  const jump = useCallback((change: JumpTarget) => setTarget({ ...change }), []);
  return <ReviewNavigation.Provider value={{ target, jump }}>{children}</ReviewNavigation.Provider>;
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
    if (kind === "Row" && (target!.path.length !== path.length + 1 || !path.every((part, index) => target!.path[index] === part))) return;
    const element = ref.current;
    element.scrollIntoView?.({ block: "center" });
    const control = element.querySelector<HTMLElement>("input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)");
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
