import { createContext, useContext } from "react";
import { useLocation } from "react-router";
import { useLatestRequest } from "../components/ui/useLatestRequest";

/** File/plugin/group navigation in one workspace shares a latest-selection guard. */
export const WorkspaceSelectionContext = createContext<(() => () => boolean) | null>(null);

export function useWorkspaceSelection() {
  const location = useLocation();
  const fallback = useLatestRequest(location.key);
  return useContext(WorkspaceSelectionContext) ?? fallback;
}
