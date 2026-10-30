import { request } from "./client";
import { documentPath, postJson } from "./requests";
import type { CollectionQuery, EditResult, FieldError, FieldValue } from "./protocol";

/** SDK query names differ from the collection endpoint's query-string names. */
export interface QueryableCollectionQuery extends Omit<CollectionQuery, "q" | "sort"> {
  text?: string;
  sortColumn?: string;
}
export interface SemanticCollectionTarget {
  collectionId: string;
  rowKey: string;
  query: QueryableCollectionQuery;
}
export type FieldBindingKind = "Primary" | "Derived";
export interface FieldPresentation { confidence?: "Verified" | "Inferred" | "Unknown"; warning?: string; description?: string }
export interface SemanticProjection {
  valueId: string; label: string; path: string[]; bindingKind?: FieldBindingKind; presentation?: FieldPresentation;
  member?: SemanticMemberContext; changed?: boolean;
}
export interface SemanticNavigationTarget {
  path: string[]; collection?: SemanticCollectionTarget; available: boolean; unavailableReason?: string;
}
export interface SemanticMemberContext { role: string; relativePath: string; groupTitle?: string; provenance?: string }
export interface SemanticReviewCoverage {
  examinedCount: number; omittedCount: number; budgetBytes: number; usedBytes: number; omittedReasons: string[];
  omittedCountIsLowerBound?: boolean;
}
export function collectionReviewQuery({ text, sortColumn, ...query }: QueryableCollectionQuery): CollectionQuery {
  return { ...query, q: text, sort: sortColumn };
}

interface ActionQueryScope {
  collectionId: string;
  query: { text?: string; filters?: Record<string, string>; sortColumn?: string; descending?: boolean; includeEmpty?: boolean };
  revision: number;
  excludedRowKeys: readonly string[];
}
export interface ActionScope { rowKeys?: readonly string[] | null; queryScope?: ActionQueryScope }
interface PlannedChange {
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
  target?: SemanticCollectionTarget;
  canonicalValueId?: string; bindingKind?: FieldBindingKind; presentation?: FieldPresentation;
  aliases?: SemanticProjection[]; member?: SemanticMemberContext;
  navigationTarget?: SemanticNavigationTarget; canNavigate?: boolean; revertReason?: string;
}
export interface SemanticChangeLog {
  changes: SemanticChange[]; complete: boolean; warnings: string[]; revision: number; coverage?: SemanticReviewCoverage;
}

export const actionReview = {
  plan: (workspace: string, document: string, actionId: string, parameters: Record<string, FieldValue>, scope: ActionScope, revision?: number) =>
    request<ActionPreview>(`${documentPath(workspace, document)}/actions/plan`, postJson({ actionId, parameters, scope, revision })),
  apply: (workspace: string, document: string, planId: string, revision: number) =>
    request<EditResult>(`${documentPath(workspace, document)}/actions/apply`, postJson({ planId, revision })),
  changes: (workspace: string, document: string) => request<SemanticChangeLog>(`${documentPath(workspace, document)}/changes`),
  revert: (workspace: string, document: string, valueId: string, revision: number) =>
    request<EditResult>(`${documentPath(workspace, document)}/changes/revert`, postJson({ valueId, revision })),
};

export function reviewValue(value: FieldValue, exists = true): string {
  return exists ? formatValue(value) : "(absent)";
}

/** Plain text for any wire value, including tagged exact numbers and union choices (F10). */
function formatValue(value: FieldValue): string {
  if (value === null) return "null";
  if (typeof value !== "object") return String(value);
  return value.kind === "exactNumber" ? value.text : `${value.type}: ${formatValue(value.value)}`;
}
