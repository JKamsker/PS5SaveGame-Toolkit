# SDK07 toolkit presets

Toolkit presets are host-owned, versioned JSON records. They live in the configured preset store and
are never read by save import/export, so selecting or deleting a preset cannot alter save bytes. The
record contains a human name, a compatibility boundary, and semantic action parameters:

```json
{
  "schemaVersion": "1",
  "name": "Refill consumables",
  "compatibility": {
    "pluginId": "example",
    "pluginVersion": "1.0.0",
    "titleIds": ["PPSA00000"],
    "gameBuild": "1.03",
    "formatVariant": "retail",
    "referencePackIdentity": "example-items@2026-10",
    "capabilitySchemaVersion": "1",
    "requiresExplicitInstanceResolution": true
  },
  "actions": [{ "actionId": "restock", "parameters": { "quantity": 10 } }]
}
```

`PresetCompatibility` is intentionally stricter than a file name or byte length. For an opt-in
`IPresetDocument`, every boundary the current capability publishes must be present and equal in the
preset; omission is incompatible. Unknown current build or reference identity remains unknown and
blocks a preset that requires it. `CapabilitySchemaVersion` is persisted separately from the host
storage `ToolkitPreset.SchemaVersion` and is compared exactly. The host owns the pinned reference
identity and ignores an unverified plugin string. `ReferencePackInfo.ContentFingerprint` and
`IReferenceContext.ContentIdentity` are host-computed SHA-256 identities over the validated manifest
and every listed file hash; they distinguish same-id/version/locale overlays and embedded/user content
without plugin I/O. A missing or ambiguous owned instance must return
`PresetInstanceResolutionStatus.Missing` or `Ambiguous`; host code must not substitute by display name,
type id, physical slot, or a session id. `PresetInstanceReference.StableId` is a semantic identifier,
never an ephemeral document/session identifier.

Plugins opt in by implementing `IPresetDocument`. `PresetActionCapability.ScopeKinds` advertises all
supported semantic scopes; `ScopeKind` remains the legacy single-scope field. The host capture UI
exposes only those advertised scopes and passes current table selection keys only for `Selected`.
An empty `Selected` selection stays empty and cannot become document scope. `CapturePreset(PresetCaptureRequest)` receives
ephemeral selected row keys and scope intent, and must return semantic targets; those row keys are never
persisted. The implementation remains stateless and performs no I/O. It resolves instance references and
calculates an `ActionPlan` without mutation, then applies that plan atomically. The host owns persistence,
title/build/plugin/reference compatibility, revision checks, and undo history. Existing plugins remain
compatible and use the bounded one-action F6 adapter while a plugin-specific adapter is being developed.

Use `PresetScopeKinds.Document`, `AllEligible`, `AllCopies`, or `Selected` for semantic scope. For an
ambiguous target, return all candidates and require an explicit `PresetInstanceChoice`; the host never
uses a positional row key, display name, type id, or old plan token as a persisted selector.

The initial API is `GET/POST /api/presets`, `PATCH/DELETE /api/presets/{id}`, and document-scoped
`POST .../presets/{id}/preview` and `/apply`. Preview is revision-bound and apply consumes the existing
single-use F6 plan token; a stale revision returns a conflict without partial mutation. A preset with
more than one action requires a plugin adapter and is rejected by the generic path rather than silently
applying only part of it.

The current web surface is a document-local Toolkit presets section. It exposes loading, empty, error,
read-only, compatibility, preview, stale and apply states and keeps parameters local until Save is
pressed. It is a foundation for a later native dialog/list and explicit owned-instance resolution UI.
It does not imply a native loadout/socket writer: FF7 must first adopt `IPresetDocument` for supported
F6 actions and provide variant/reference-pack identity and owned-instance candidate resolution.

For the planned FF7 adapter, the stable action identifiers and typed parameter contracts are:

| action | parameters | supported semantic scopes |
| --- | --- | --- |
| `inventory/restock-existing` | `quantity` (integer 1..99) | `Document`, `AllEligible`, `Selected` |
| `materia/set-ap-within-tier` | `type` (raw catalogue id), `ap` (bounded integer) | `Document`, `AllCopies`, `Selected` |
| `materia/set-stars` | `type` (raw catalogue id), `stars` (integer 1..5) | `Document`, `AllCopies`, `Selected` |
| `character/recover-current-vitals` | no parameters | `Document`, `AllEligible`, `Selected` |
| `character/set-level` | `level` (bounded integer) | `Document`, `AllEligible`, `Selected` |

`CapturePreset` converts the selected physical row keys into semantic target references; those
references and any plugin-owned scope metadata are separate from the F6 parameter values above and
are never positional save selectors. The resulting `PresetInstanceReference` values are persisted only
when they are stable semantic identities. A selected row key is a request-time input only. Rebirth owned rows
must resolve through the adapter's detail identity: zero candidates is `Missing`, multiple candidates is
`Ambiguous` with explicit `PresetInstanceChoice` values, and one candidate is `Resolved`. Remake
materia has no proven portable owned-instance identity, so an adapter should reject `Selected` for that
case and offer `AllCopies` where supported. Native socket/materia loadouts and structural writers are
outside this initial action contract.

FF7 compatibility must pin title id, `formatVariant` (`Remake`, `Rebirth`, or `RebirthDemo`), plugin
version, schema/file role, and `referencePackIdentity`. Retail and demo saves have shared dimensions but
different variants and therefore cannot cross-apply. The reference identity is the host-generated
`sha256:<hex>` content fingerprint, not a display-label hash and not a plugin-read manifest path.

Accessibility guidance follows the [WAI-ARIA status message guidance](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)
and [focus appearance guidance](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance). The
current panel uses native controls, visible focus, labelled status/error regions, and a narrow-screen
stacked layout.

Resolution is an exact host contract: each requested semantic reference must occur exactly once in
the adapter result, with no injected or dropped targets. Missing and unsupported results have no
candidates; an ambiguous result has at least two current candidates; a normal resolved result has
exactly one current candidate and needs no choice. Choice keys and candidate ids are checked against
the same revision-bound ambiguous result before a second resolution, and the chosen result must
re-resolve to one `Resolved` candidate containing the selected identity.
Candidate ids and choices are transient preview data and are never written to a preset. A malformed
or incomplete result receives a typed preview error and cannot create an apply token.

Preset JSON routes use finite pre-binding envelope budgets so chunked and declared oversized bodies
receive a typed 413 before JSON materialization: create 512 KiB, capture 256 KiB, preview 256 KiB,
apply 64 KiB, and host-library rename 16 KiB. The route middleware reads lengthless HTTP/2 bodies
through the same max-plus-one bounded stream. Workspace/pack upload limits and transfer package
handlers remain independent. Generic compatibility rows
consult the current indexed planning and mutating action policy, so unknown, inspection-only, or
non-plannable actions are visibly unsupported before Preview is enabled. Host rename remains
available when the opened document is read-only because it changes only the external preset library.

`PresetCapability.MaxActions` limits the number of actions stored in one preset and applied as one
undo unit; it does not hide published descriptors. The host bounds the descriptor list separately
and validates every action id, label, scope, parameter id, typed specification, and metadata string.
Durable records accept only the four `PresetScopeKinds` values. Capture requests normalize selection
keys away for non-`Selected` scopes before invoking a plugin adapter.

Before a capability reaches a document DTO, compatibility summary, or UI, the host admits it into a
bounded detached snapshot. The snapshot limits graph depth and node count, rejects cycles, duplicate
union/enum/flag entries, invalid flag widths or bit positions, and oversized metadata. A malformed
capability is reported as disabled (`capabilityMalformed` at the API boundary); it never falls back to
the generic semantic path and never invokes capture, resolution, planning, or apply. When a refreshed
document publishes a different action descriptor or parameter contract, the panel resets to that
descriptor's first advertised scope and fresh typed defaults while preserving an unrelated draft.

The panel presents the server-owned current title, preset kind, readiness and apply state. Saved
presets can be filtered by name/action, title, build, plugin, compatibility and update age; the
bounded list reports its matching count and first-page limit. Preview changes and skipped rows are
inside a labelled, keyboard-scrollable region so narrow screens do not hide the review surface.
