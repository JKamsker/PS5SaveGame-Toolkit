import { useMemo } from "react";
import type { ActionNode, FieldNode, FieldValue } from "../../api/protocol";

/** Action parameters are writable even when their document-field equivalents are read-only. */
export function useWritableActionParameters(node: ActionNode) {
  return useMemo(() => (node.parameters ?? []).map(parameter => ({
    ...parameter, spec: { ...parameter.spec, readOnly: false },
  })), [node.parameters]);
}

export function actionParameterDefaults(parameters: readonly FieldNode[]): Record<string, FieldValue> {
  return Object.fromEntries(parameters.map(parameter => [parameter.valueId, parameter.value]));
}
