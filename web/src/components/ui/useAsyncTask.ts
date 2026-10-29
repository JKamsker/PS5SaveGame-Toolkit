import { useEffect, useMemo, useState } from "react";

/** A synchronous submit guard with a lifetime for suppressing obsolete UI side effects. */
export function useAsyncTask(key?: unknown) {
  const lifetime = useMemo(() => ({ active: true, pending: false }), [key]);
  const [pending, setPending] = useState<typeof lifetime | null>(null);
  useEffect(() => {
    lifetime.active = true;
    return () => { lifetime.active = false; };
  }, [lifetime]);

  const run = async (task: (isCurrent: () => boolean) => Promise<void> | void) => {
    if (!lifetime.active || lifetime.pending) return;
    lifetime.pending = true;
    setPending(lifetime);
    try {
      const result = task(() => lifetime.active);
      if (result) await result;
    } finally {
      lifetime.pending = false;
      if (lifetime.active) setPending(null);
    }
  };
  return { busy: pending === lifetime, run };
}
