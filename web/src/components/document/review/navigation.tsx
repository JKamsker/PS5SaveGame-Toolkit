import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { SemanticChange } from "../../../api/actionReview";

interface Navigation { target: SemanticChange | null; jump: (change: SemanticChange) => void }
const ReviewNavigation = createContext<Navigation>({ target: null, jump: () => {} });
const ReviewPathContext = createContext<readonly string[]>([]);

export function ReviewNavigationProvider({ children }: { children: ReactNode }) {
  const [target, setTarget] = useState<SemanticChange | null>(null);
  const jump = useCallback((change: SemanticChange) => setTarget({ ...change }), []);
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
  const { target } = useReviewNavigation();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (target?.valueId !== valueId || !ref.current) return;
    const element = ref.current;
    element.scrollIntoView?.({ block: "center" });
    const control = element.querySelector<HTMLElement>("input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)");
    (control ?? element).focus({ preventScroll: true });
  }, [target, valueId]);
  return ref;
}
