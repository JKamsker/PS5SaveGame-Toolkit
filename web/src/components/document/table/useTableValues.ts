import { useCallback, useMemo, useSyncExternalStore } from "react";
import type { FieldNode, TableRow } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import type { ValueOf } from "./cellText";

/** Only direct field cells contribute live values to table search, facets and row labels. */
export function useTableValues(rows: readonly TableRow[]): ValueOf {
  const { store } = useDocument();
  const subscription = useMemo(() => {
    const ids = [...new Set(rows.flatMap(row => Object.values(row.cells)
      .filter((cell): cell is FieldNode => cell.type === "field").map(cell => cell.valueId)))];
    let version = store.valueVersion;
    return {
      getSnapshot: () => version,
      subscribe: (listener: () => void) => {
        const changed = () => { version = store.valueVersion; listener(); };
        const unsubscribe = store.subscribeValues(ids, changed);
        // Catch changes between rendering and subscribing, including subscription replay.
        changed();
        return unsubscribe;
      },
    };
  }, [store, rows]);
  const version = useSyncExternalStore(subscription.subscribe, subscription.getSnapshot);
  return useCallback((node: FieldNode) => store.value(node.valueId, node.value), [store, version]);
}
