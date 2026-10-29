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
    "requiresExplicitInstanceResolution": true
  },
  "actions": [{ "actionId": "restock", "parameters": { "quantity": 10 } }]
}
```

`PresetCompatibility` is intentionally stricter than a file name or byte length. A plugin that has
variant or reference-pack evidence should publish those values through `IPresetDocument` and include
them when creating a preset. `ReferencePackInfo.ContentFingerprint` and
`IReferenceContext.ContentIdentity` are host-computed SHA-256 identities over the validated manifest
and every listed file hash; they distinguish same-id/version/locale overlays and embedded/user content
without plugin I/O. A missing or ambiguous owned instance must return
`PresetInstanceResolutionStatus.Missing` or `Ambiguous`; host code must not substitute by display name,
type id, physical slot, or a session id. `PresetInstanceReference.StableId` is a semantic identifier,
never an ephemeral document/session identifier.

Plugins opt in by implementing `IPresetDocument`. `CapturePreset(PresetCaptureRequest)` receives
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

Accessibility guidance follows the [WAI-ARIA status message guidance](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)
and [focus appearance guidance](https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance). The
current panel uses native controls, visible focus, labelled status/error regions, and a narrow-screen
stacked layout.
