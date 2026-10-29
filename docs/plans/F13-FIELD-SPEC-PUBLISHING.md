# F13: field specs independent of views — partially delivered proposal

Issue #33 permits a design deliverable for this item. **Status — 2026-10-29:** storage-only
binding registration shipped in `a1c08c0`, through `BoundSaveDocument.RegisterField(...)` and
`Field(valueId, label)`. That part of the proposal is done. Host metadata resolution, primary/alias
classification and Core lifecycle integration below remain proposed; plugins must retain their
preflight guards. See [the shipped contract](../PLUGIN_GUIDE.md#contract-srcsavetoolkitsdk) and
[SDK-02 / SDK-32](../SDK_REQUESTS.md#prioritized-inventory).

## What the current code actually does

`DocumentSession.Revert` already replays action-reported value IDs that have **no** indexed spec.
It only excludes IDs whose known spec is read-only. The regression test
`SessionAndExportTests.Undo_RevertsActionChangesToValuesNeverShownInTheView` covers that behavior.
Thus “undo only knows fields exposed in views” is too broad for this revision of the framework.

The unresolved problem is publishing enough information to make that replay *reliably correct*.
`ViewIndex` learns specs only from eager, expanded, queried or semantic-review-discovered fields.
It cannot classify an unexposed ID as an editable primary versus a derived/read-only alias.
`BoundSaveDocument.ApplyEdits` also needs a registered writable binding, independently of the Core
index. Plugins can now register it without rendering a node, but that does not publish metadata
to Core. `SemanticReview` can retain an action's old value for previously unexposed IDs before
structural edits, but lacks canonical labels/navigation and conservatively disallows individual
reversion for those fallback records. Structural identity changes further limit matching.

Pragmata's `QuantityActions.Plan` rejects selected quantities without `HasPrimaryBinding` before
generating a plan. Publishing a spec alone would not register its primary setter or resolve
aliases, and removing that guard would therefore be premature. F8 whole-query resolution helps
only when querying the collection actually publishes all required primary bindings.

## Proposed optional capability

Add a separate `IFieldMetadataDocument`, leaving `ISaveDocument` unchanged. It resolves an explicit,
bounded list of stable IDs to immutable metadata: ID, canonical label, `FieldSpec`, optional
navigation path, and whether an ID is a primary writable value or a derived alias. The first
version should use existing `FieldSpec.ReadOnly` for derived/read-only metadata, without adding
an alternative write permission. Unknown IDs return no descriptor. It must not enumerate every
field or build/expand a view.

Resolving metadata must guarantee `GetValue` works, and for a writable field that `ApplyEdits`
already has the canonical setter and plugin validation available without displaying its page.
The delivered `BoundSaveDocument.RegisterField(valueId, spec, get, set, validate)` already registers
bindings independently from constructing `FieldNode`; `Field` reuses it. `Field(valueId, label)`
renders a registered binding later. Registration does not call the getter, and structural changes
or restores clear registrations. Plugin-owned registration remains local bookkeeping with no save-value
changes. A future descriptor without a usable binding violates the proposed metadata capability's contract.

## Core integration and lifecycle

1. F6 resolves descriptors for all planned changes before exposing a usable preview. Resolve in
   batches of at most 200 IDs, with the existing action row/preview byte budgets bounding the
   complete operation. Missing descriptors, conflicting duplicate IDs/specs, or unavailable
   primary bindings reject preflight. Preserve current behavior for documents without the capability.
2. Store validated descriptors in a dedicated index composed by `ViewIndex`. When a view later
   exposes an ID, require its spec to agree; never let a browser request create metadata. Action
   parameter IDs stay separate from document-field IDs.
3. On apply, re-resolve against the current revision and compare metadata and planned effects.
   Record every primary and derived `ValueChange`, but replay only writable primaries. Plugins
   still recompute derived values atomically, and report their changes. Metadata is not permission
   to bypass plugin-specific dependency checks.
4. After structural change/undo/redo, invalidate the metadata cache alongside the view index and
   retire vanished IDs. Resolve descriptors against the restored model before replaying subsequent
   steps. Stable logical IDs, not array positions, remain mandatory.
5. Extend semantic review's original document walk with targeted metadata resolution for planned
   IDs, using the same pinned reference packs and review memory budget. Capture originals before
   mutation. Missing/over-budget metadata explicitly marks review incomplete, never “unchanged”.
   Show canonical labels even when no page is loaded; only offer navigation for an actual view path.

Avoid advertising this as a tiny spec lookup: replay, binding registration, alias policy,
original-value capture, cache invalidation and group-document forwarding must agree. Group
staging must resolve against the candidate model, never bindings from a previous publication.

## Required verification before adoption

- More than 200 inventory rows; plan/apply without opening target pages; one undo/redo restores
  exact serialized bytes, primary quantities and all derived aliases.
- Primary and derived IDs absent from views; the primary replays once and derived values recompute.
- Partial metadata, contradictory specs, unknown IDs, missing setters and budget overflow reject
  before any edit; absent capability retains backward compatibility.
- Removed/restored records, stable IDs, stale previews and repeated structural undo/redo do not
  retain retired descriptors or setters captured from previous models.
- Semantic review has correct original/current labels and values for unloaded pages and reports
  incomplete coverage honestly when its bounded baseline cannot include them.
- Grouped documents resolve/restage bindings correctly across members and publication boundaries.

This deserves a follow-up framework issue and plugin adoption round. The current Pragmata guard
should remain until the capability and these checks ship together.
