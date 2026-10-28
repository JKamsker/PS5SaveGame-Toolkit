import { request } from "./client";
import type { EditResult, FieldError, FieldValue } from "./protocol";

export interface ActionQueryScope {
  collectionId: string;
  query: { text?: string; filters?: Record<string, string>; sortColumn?: string; descending?: boolean };
  revision: number;
  excludedRowKeys: readonly string[];
}
export interface ActionScope { rowKeys?: readonly string[] | null; queryScope?: ActionQueryScope }
export interface PlannedChange {
  valueId: string; label: string; oldValue: FieldValue; newValue: FieldValue;
  derived: boolean; oldExists: boolean; newExists: boolean;
}
export interface ActionPreview {
  planId?: string; revision: number; scope: ActionScope; stale?: boolean;
  plan: { changes: PlannedChange[]; skippedRows: { rowKey: string; reason: string }[]; warnings: string[]; errors: FieldError[] };
}
export interface SemanticChange {
  valueId: string; label: string; path: string[]; originalValue: FieldValue; currentValue: FieldValue;
  originalExists: boolean; currentExists: boolean; canRevert: boolean;
}
export interface SemanticChangeLog { changes: SemanticChange[]; complete: boolean; warnings: string[]; revision: number }

const base = (workspace: string, document: string) => `/workspaces/${encodeURIComponent(workspace)}/documents/${encodeURIComponent(document)}`;
const post = (body: unknown): RequestInit => ({ method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });

export const actionReview = {
  plan: (workspace: string, document: string, actionId: string, parameters: Record<string, FieldValue>, scope: ActionScope, revision?: number) =>
    request<ActionPreview>(`${base(workspace, document)}/actions/plan`, post({ actionId, parameters, scope, revision })),
  apply: (workspace: string, document: string, planId: string, revision: number) =>
    request<EditResult>(`${base(workspace, document)}/actions/apply`, post({ planId, revision })),
  changes: (workspace: string, document: string) => request<SemanticChangeLog>(`${base(workspace, document)}/changes`),
  revert: (workspace: string, document: string, valueId: string, revision: number) =>
    request<EditResult>(`${base(workspace, document)}/changes/revert`, post({ valueId, revision })),
};

export function reviewValue(value: FieldValue, exists = true): string {
  return exists ? (value === null ? "null" : String(value)) : "(absent)";
}
