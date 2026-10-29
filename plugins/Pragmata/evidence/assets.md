# Asset round — 2026-10-29

Issue [#21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21), branch
`plugin/pragmata-assets`; plugin 0.6.0, embedded evidence pack 1.4.0.

## Delivered and deliberately unresolved

- 29 images: 14 items and 15 common weapons. All are 64×64 WebP; 13 derive from
  UI icons and 16 from transparent renders. The lazy illustrated F3 table and
  browse-only F4 catalogue use stable wiki file identities. All 90 numeric save
  item IDs remain unmapped; no icon/name is attached to an unproven save ID.
- 15 supported descriptions: nine short official game-description excerpts
  transcribed by the wiki, six explicitly labelled wiki summaries. Fourteen image
  references have no supported description. Game-build matching is unverified.
- One 2048×585 Cradle WebP background, **45,564 bytes**, with 26 community
  escape-hatch locations. Original image-pixel coordinates scale by `2048/7168`
  and `585/2048`; the distinct scales account for output-height rounding. Saved
  checkpoint hashes stay unpositioned, retain navigation to existing fields and
  have Unknown saved states. Reference locations likewise have Unknown unlock/
  completion state, with Inferred community-coordinate confidence.
- **0 of 43 mission descriptions resolved.** The reviewed PC mission JSON has
  name/description GUIDs, not localized strings. REE's technical string/schema/
  enum dumps and the runtime resolver source do not provide the missing table.
  Exact-GUID and GitHub localization/datamine searches found no usable public
  source. Display order is insufficient to assert a match. Existing honest
  mission labels and graph semantics are preserved.

Sources, revisions, ownership notices, transformations and per-image fingerprints
are in the [asset sidecar](../SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/visual-reference.source.md)
and its JSON ledger, the README, notices, Info.References, field/image attribution
and the automatic Sources panel. The prior publisher-asset licence refusal is
resolved for this subset by the 2026-10-29 owner decision. The
[central register](../../../docs/LICENCE_EXCLUSIONS.md) now records the resolved
publisher-asset imports and the separate Itemaps coordinate decision.

## Exact footprint and load measurement

New binary assets: **99,634 bytes** total: **54,070 icon bytes** + **45,564 map
bytes**. All new embedded files, including the two catalogues and source
sidecars/ledger: **144,969 bytes**. This excludes growth of the existing manifest
and non-embedded README/evidence/source code. No original-size images, complete
wiki, sample payloads or downloaded code are committed.

Measured with a temporary Release/net10.0 stopwatch harness outside the repo.
The largest sample is `data010Slot.bin`, **3,367,308 bytes**, read from the
external archive into memory before measuring. The timed interval constructs a
`ReferencePackStore`, pins the pack, opens the save and builds the initial view.
Allocation count uses `GC.GetTotalAllocatedBytes(true)` around that interval;
full collection occurs before each of six runs. First run includes cold JIT/
initialization; the other five recreate the store to assess warmed code.

| Measurement | Before | After |
| --- | ---: | ---: |
| First load | 319.498 ms | 353.9891 ms |
| First-load managed allocations | 69,022,208 bytes | 72,091,488 bytes |
| Median of five subsequent runs | 149.2521 ms | 157.4494 ms |
| Median subsequent allocations | 68,300,880 bytes | 71,438,672 bytes |

These are quick local measurements with normal host noise, not a performance
guarantee. A production store validates/loads the pack once and shares the same
catalogue and asset objects between pinned contexts; the tests verify that
sharing. Library rows and map background access are deferred until expansion.
No per-open network, filesystem reads or image decoding is added to the plugin.

## Validation

With `SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`
exported in each command:

- Plugin suite: **139 passed, 0 failed, 0 skipped** (all 12 distinct sample files).
  Existing crypto, conformance, editing, diff, planning and undo tests pass.
- API build: **0 warnings, 0 errors**.
- New tests verify every asset byte hash, detected WebP type and encoded
  dimensions; all catalogue asset IDs resolve; exact size budget; 29 displayed
  icons and attribution; read-only browse fields; unchanged save/history;
  shared asset/catalogue instances; missing-pack fallback; all-sample map
  validation, transformed bounds and separation of saved/reference markers.
- Reviewed a generated thumbnail contact sheet and atlas preview outside the
  repository. No full application/browser interaction test is claimed.
- Visual pack regeneration is deterministic; C# files remain under 300 lines;
  whitespace checks pass. Changes are confined to `plugins/Pragmata`.

No push or merge. Console validation and maturity remain tracked separately in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
