import { useEffect, useMemo, useState } from "react";

/** A synchronous submit guard with a lifetime for suppressing obsolete UI side effects. */
export function useAsyncTask(key?: unknown) {
  const lifetime = useMemo(() => ({ active: true, pending: false }), [key]);
  const [pending, setPending] = useState<typeof lifetime | null>(null);
  useEffect(() => {
    lifetime.active = true;
    return () => { lifetime.active = false; };
  }, [lifetime]);

  const run = async (task: (isCurrent: () => boolean) => Promise<void>) => {
    if (!lifetime.active || lifetime.pending) return;
    lifetime.pending = true;
    setPending(lifetime);
    try {
      await task(() => lifetime.active);
    } finally {
      lifetime.pending = false;
      if (lifetime.active) setPending(null);
    }
  };
  return { busy: pending === lifetime, run };
}
