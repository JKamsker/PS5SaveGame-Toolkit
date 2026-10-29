import type { FieldValue } from "../../api/protocol";
import { useAsyncTask } from "../ui/useAsyncTask";
import type { FieldControlProps } from "./FieldControl";

/** Local action parameters commit synchronously; server-backed controls stay busy until settled. */
export function useValueCommit(id: string, onCommit: FieldControlProps["onCommit"]) {
  const { busy, run } = useAsyncTask(id);
  return { busy, commit: (value: FieldValue) => run(() => {
    const result = onCommit(value);
    if (typeof result !== "boolean") return result.then(() => {});
  }) };
}
