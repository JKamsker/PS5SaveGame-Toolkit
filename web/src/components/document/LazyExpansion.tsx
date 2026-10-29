import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { DocumentContext } from "../../state/documentStore";

const MAX_AUTOMATIC_LAZY_LOADS = 8;
const MAX_AUTOMATIC_LAZY_DEPTH = 1;
export const MAX_CONCURRENT_LAZY_REQUESTS = 4;

const AutomaticLoads = createContext<Set<string> | null>(null);
const LazyDepth = createContext(0);

/** All renderers in one view share a budget, including tabs and table details. */
export function LazyExpansionBoundary({ children }: { children: ReactNode }) {
  const parent = useContext(AutomaticLoads);
  return parent ? children : <LazyExpansionScope>{children}</LazyExpansionScope>;
}

function LazyExpansionScope({ children }: { children: ReactNode }) {
  const document = useContext(DocumentContext);
  const budget = useMemo(() => new Set<string>(), [document?.workspaceId, document?.documentId]);
  return <AutomaticLoads.Provider value={budget}>{children}</AutomaticLoads.Provider>;
}

/** Count lazy boundaries rather than eager grouping sections or tabs. Manual loads never reset depth. */
export function LazyDescendants({ children }: { children: ReactNode }) {
  const depth = useContext(LazyDepth);
  return <LazyDepth.Provider value={depth + 1}>{children}</LazyDepth.Provider>;
}

export function useAutomaticLazyExpansion(lazyId: string | undefined): boolean {
  const budget = useContext(AutomaticLoads);
  const depth = useContext(LazyDepth);
  const [admission, setAdmission] = useState<{ budget: Set<string>; lazyId: string; allowed: boolean }>();
  useEffect(() => {
    if (!lazyId || !budget || depth >= MAX_AUTOMATIC_LAZY_DEPTH) return;
    // Allocate after commit, never during rendering. Keep grants for the mounted view's lifetime,
    // using published IDs so returning to a tab reuses its grant. StrictMode replay is idempotent.
    const allowed = budget.has(lazyId) || budget.size < MAX_AUTOMATIC_LAZY_LOADS;
    if (allowed) budget.add(lazyId);
    setAdmission({ budget, lazyId, allowed });
  }, [lazyId, budget, depth]);
  return Boolean(lazyId) && admission?.budget === budget && admission.lazyId === lazyId
    && admission.allowed && depth < MAX_AUTOMATIC_LAZY_DEPTH;
}
