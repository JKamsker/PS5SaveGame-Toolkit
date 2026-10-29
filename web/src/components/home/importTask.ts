import { createContext, useContext } from "react";
import { useAsyncTask } from "../ui/useAsyncTask";

/** ZIP inputs and all game tiles share one import slot while the home page is mounted. */
export const ImportTaskContext = createContext<ReturnType<typeof useAsyncTask> | null>(null);

export function useImportTask(key: unknown) {
  const local = useAsyncTask(key);
  const shared = useContext(ImportTaskContext);
  if (!shared) return local;
  return {
    busy: shared.busy || local.busy,
    run: (task: (isCurrent: () => boolean) => Promise<void>) => shared.run(sharedCurrent =>
      local.run(localCurrent => task(() => sharedCurrent() && localCurrent()))),
  };
}
