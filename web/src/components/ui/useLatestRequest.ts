import { useCallback, useEffect, useMemo } from "react";

/** Starting a request retires older observers without replaying or cancelling server mutations. */
export function useLatestRequest(key: unknown) {
  // Changing the owner key retires observers without cancelling their server mutations.
  const lifetime = useMemo(() => ({ active: true, sequence: 0 }), [key]);
  useEffect(() => {
    lifetime.active = true;
    return () => { lifetime.active = false; };
  }, [lifetime]);
  return useCallback(() => {
    const sequence = ++lifetime.sequence;
    return () => lifetime.active && sequence === lifetime.sequence;
  }, [lifetime]);
}
