import { createContext, useContext } from "react";
import { useAsyncTask } from "../ui/useAsyncTask";

/** ZIP inputs and all game tiles share one import slot while the home page is mounted. */
export const ImportTaskContext = createContext<ReturnType<typeof useAsyncTask> | null>(null);
/** Source intent retires observers independently of the still-pending shared submit slot. */
export const ImportIntentContext = createContext<(() => () => boolean) | null>(null);

export function useImportTask(key: unknown) {
  const local = useAsyncTask(key);
  const shared = useContext(ImportTaskContext);
  const beginIntent = useContext(ImportIntentContext);
  const run = (task: (isCurrent: () => boolean) => Promise<void>) => local.run(localCurrent => {
    const intentIsCurrent = beginIntent?.() ?? (() => true);
    return task(() => localCurrent() && intentIsCurrent());
  });
  if (!shared) return { ...local, run };
  return {
    busy: shared.busy || local.busy,
    run: (task: (isCurrent: () => boolean) => Promise<void>) => shared.run(sharedCurrent =>
      run(localCurrent => task(() => sharedCurrent() && localCurrent()))),
  };
}
