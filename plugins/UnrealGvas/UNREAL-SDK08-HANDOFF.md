# UnrealGvas SDK-08 handoff

This handoff records the bounded plugin preparation that can be reviewed and merged
independently of the SDK-08 framework change.

| Item | Value |
| --- | --- |
| Base | `e76e51035fec308a56e5c26b5d97a9c08089f881` (`main`) |
| Head | `5cdb622a` (`feat(unrealgvas): adopt targeted field metadata`) |
| Branch | `t3/unreal-sdk-roadmap` |
| Worktree | `C:\Users\Jonas\.t3\worktrees\PS5SaveGame-Toolkit\t3-c7877249` |
| Plugin path | `plugins/UnrealGvas/` |
| Preparation PR | https://github.com/JKamsker/PS5SaveGame-Toolkit/pull/53 |
| Framework dependency | SDK-08 draft PR https://github.com/JKamsker/PS5SaveGame-Toolkit/pull/52 at immutable head `9d6dccb46c6e57e040d1a12a613ef9cfbc929d1a` |

## Fulfilled in this handoff

- `UnrealGvasDocument` implements SDK-02 `IFieldMetadataDocument`.
- `GvasScalarBindingPolicy` is the single policy used by rendered raw scalar fields and
  metadata-only registration. It preserves effective document read-only state, integrity
  protection, actor-length protection, native specs, setters and validators.
- Resolution validates null/empty IDs and the 200-request limit before any registration,
  counts duplicates toward the limit, deduplicates exact ordinal IDs, and looks up only
  requested current `property/...` IDs. Containers, opaque nodes, actor observations and
  unknown IDs are omitted. It does not build, expand, query or perform I/O.
- `GvasQueryableDocument` forwards metadata resolution to the raw document that owns the
  live bindings.
- Structural remove-last and memento restore tests prove old registrations are retired and
  current model bindings are recreated only after a fresh targeted resolution. No-op
  metadata discovery preserves complete original bytes.
- README status is explicit: generic/Jedi/Sackboy named F8 projections remain text until
  SDK-08 `FieldReferenceNode` is available; no framework code or new native writer is in
  this plugin preparation.

## Validation from this head

- `dotnet test --project plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests --configuration Release`:
  **293 passed, 0 failed, 96 skipped**. Skips are the existing absent private sample archives
  (including PPSA01288, PPSA02199, PPSA01603 and other sample bundles); no sample bytes were
  copied, logged or committed.
- `dotnet test --project plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests --configuration Release --filter FullyQualifiedName~GvasFieldMetadataTests`:
  **5 passed, 0 failed, 0 skipped**.
- `dotnet build plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/SaveToolkit.Plugins.UnrealGvas.csproj --configuration Release`:
  **0 warnings, 0 errors**.

## Pending after SDK-08 merge

After PR #52 is merged into `origin/main`, merge `origin/main` into this branch normally,
then implement and test the separate bounded projection layer:

- Replace eligible genuine raw scalar `value` cells in `gvas/properties`, Progression,
  Settings and Jedi settings with direct `FieldReferenceNode(TargetValueId)`. Keep actor tags,
  observed strings, lengths, checksums, opaque/unmapped rows and schema-caption references
  as their honest existing text/read-only forms.
- Add only the SDK-08 `FieldMetadata.Presentation` annotations supplied by the canonical
  binding. Preserve confidence, warning and description exactly; reject conflicts and never
  invent a confidence or read/write policy in a projection.
- Keep complete live search/filter/sort/identity/provenance semantics. Use full canonical
  `FieldValue`/spec data in normalized references, retain the bounded text fallback for long
  values, and verify normalized pages and combined views stay within the host 2 MiB response
  budget without clipping a `FieldValue`.
- Complete the readiness cases for Jedi `HDRBrightness`, Sackboy `CollectabellsStored`,
  malformed/duplicate/201-ID targeted lookup, shared live values/errors/history, atomic mixed
  batches, Unicode and embedded-NUL restoration, stale structural references, undo/redo exact
  bytes, and honest actor/opaque/unmapped fallbacks. Sample-dependent cases must skip when the
  private archives are absent.
- Update any named-catalogue read-only wording only where a direct canonical reference now
  makes an existing writable scalar editable; retain inferred meaning, unknown integrity and
  experimental warnings. Do not claim SDK-08 adoption from this metadata-only preparation.
