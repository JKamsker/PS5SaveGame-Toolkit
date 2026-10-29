# Historical community-source unblock — owner decision 2026-10-28

Issue [#21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21), branch
`plugin/pragmata-unlicensed`. The owner decision removes the previous
licence-only refusal of community data. Existing independently implemented
crypto/parser algorithms need no copied upstream code or replacement.
This report records pack 1.3.0/plugin 0.5.1. The later [asset round](assets.md)
supersedes the publisher-asset exclusion and unpinned/external-only map statements
below: pack 1.4.0 imports attributed wiki text/images and SHA-pinned Cradle
art/coordinates under the 2026-10-29 owner policy. Saved-ID associations remain
unproven. The current README and central licence register record those imports.

## Imported facts

All inputs are from kvasszn / REE Save Editor, revision
`4125e497f1ae84cc746bb4a99da612c48bea161e`, with praydog credited for dump tooling.
The [pack sidecar](../SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/community.source.md)
records exact input URLs, SHA-256 hashes, retrieval date and transformations.
Each new dataset's licence field is exactly:
**No licence published; reused with attribution per owner decision 2026-10-28**.

- `strings_pragmata.txt`: restore five hash-verified technical names: `Slot`,
  `Detail`, `_IsFirstGetWeapon`, `_IsFirstGetActiveSkill`, `_AcquireContentsHashes`.
  All five hashes occur in the supplied samples. Their resource/catalogue and
  provenance remain separate from the original 408 MIT names (413 total).
- `rszpragmata.json`: 459 class/field declarations selected by existing known
  save identifier hashes, including original/storage types, array flags and
  declared size/alignment. These describe the community schema; not every PC
  declaration must exist in each PS5 build, and declared offsets/alignment do
  not replace the existing byte-verified typed-stream parser.
- `enumspragmata.json`: 230 numeric values across eight declared types:
  `app.GadgetSlotType`, `app.ObjectiveState`,
  `app.structure.StructureDataResetTiming`,
  `app.MeshPartsControlPropDriver.PresetBits`,
  `app.MeshMaterialControlObject.UseParamBits`, `via.hid.GamePadButton`,
  `via.hid.KeyboardKey`, `via.hid.MouseButton`. Aliases remain attached to their
  exact value; indices and unrelated hashes are never interpreted as enum IDs.

At this revision the F2 pack is 1.3.0; plugin version 0.5.1 remains Experimental. Matching
primitive/raw descriptions include schema and exact enum labels with attribution
and Inferred semantics. Typed enum uint32 keys normalize the signed reader's
negative representation only for lookup. Stored values, editor specs, unknown
values/flag combinations and all experimental warnings remain intact. No legal
limits, ownership, completion, prerequisite or PS5 gameplay effects are inferred.
Catalogue metadata also credits the restored names shown in titles/summaries.

## Scope and exclusions

The audit covered the README, notices, all source sidecars and evidence notes.
All three previously research-only unlicensed dumps are now represented by
useful factual subsets. Proprietary localized game text, art/icons/screenshots
remain excluded in the README's Licence exclusions table. No NC/no-redistribution
data or GPL-2.0-only code was imported. No upstream code was executed or copied.
The generator is independently written and only reads pinned data/metadata;
downloads stayed under `%TEMP%/pragmata-unlicensed`, outside the repository.

Wiki/runtime sources lack exact save-ID mappings. The external map still lacks
a reproducible pinned dataset and checkpoint-hash/marker crosswalk; this is not
a licence-only data refusal. No map coordinates or guessed completion states
were added. Full dump redistribution and unrelated enum imports are unnecessary
for these known save fields. All changes stay under `plugins/Pragmata`.

## Verification

- `$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests -v minimal`:
  **135 passed, 0 failed, 0 skipped**, all 12 distinct samples.
- Same artifact environment with
  `dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -v quiet`:
  **0 warnings, 0 errors**.
- New tests cover pack validity/licence isolation, exact source pinning, identifier
  hash reproduction, every selected class/field hash, aliases, sample occurrence,
  schema/enum cross-indexing, exact type/value/class guards, missing-context/raw
  fallback, unchanged values/bounds/warnings, and all-sample byte identity.
- The complete existing conformance, malformed-input, edit/reopen/reversal,
  query/planning/undo and canonical decoder suites continue to pass.
- SHA-checked generation reproduces every reference/resource JSON byte-for-byte
  after LF normalization. Whitespace check passes; all C# files remain below 300 lines.

## Remaining owner checks

No new SDK request or permission gate. Existing [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
checks remain: load/re-save/reload an unchanged export, then one existing quantity,
one item-ID replacement and one primitive in each enabled family; verify actual
effects and sibling files. Supply three independent one-event before/after pairs
plus unchanged controls and legal-boundary pairs for claimed gameplay meanings.
To promote these enum hints, capture the exact class/field path, raw before/after
value, displayed state, game build/locale/platform and corresponding PS5 saves.
Localized item names/caps still require an exact numeric-ID crosswalk. Only the
owner can establish console acceptance and promote maturity to Stable.
