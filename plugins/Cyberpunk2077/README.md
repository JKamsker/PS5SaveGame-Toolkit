# Cyberpunk 2077 (`cyberpunk2077`)

Title: **PPSA04027**. Plugin **0.7.0**, maturity: **Experimental**. Writers cover five player attributes,
unspent attribute/perk/relic points, money, named player stacks, raw Level/Street Cred/XP/thresholds,
existing raw fact values, and F6 planned stack actions. Edits are
restricted to **save 265 / game 2210 / archive 195** (the supplied PS5 2.21 save). Scriptable progression
also requires **RED package 4 / flags 0 / sections 6**. Unsupported versions show a reason and retain
the applicable read-only explorers. No console load has been tested; only a human can set Stable.

Roadmap round 2 adds the evidence-supported read-only parts of Phases 2, 4 and 5: extended inventory,
equipment/cyberware slots, item/part modifiers, skills/perks/budgets, merged facts, garage/travel
status and opaque journal diagnostics. F8 queries cover each complete collection. Existing F2/F4
catalogs retain **5,248 item / 19,357 fact / 105 vehicle / 232 travel identifiers**, with source hashes.
A separate CC BY-SA 3.0 pack adds **15 friendly base-record labels** from six pinned wiki revisions.
The inventory identifies **22/22** current items; **1,129/2,750** saved fact entries resolve to names.
The merged facts view has **20,978 rows**, preserving unknown saved hashes and catalog-only facts.
Equipment, cyberware, perk, garage, travel and journal views remain read-only. Gameplay effects,
slot eligibility/capacity and exact quest completion still need controlled captures and console evidence.
See [REFERENCES.md](REFERENCES.md) and [EVIDENCE.md](EVIDENCE.md) for contracts and exact next steps.

The completion round adds lazy **F9 stored-perk graphs and link-first travel maps**, **F10 nullable
fact values**, overlapping-sibling refusal and indexed equipment/world/stat joins. Every spatial
entry links to its own read-only details, even before F8 rows have been loaded. See the full
[requirement and acceptance audit](COMPLETION.md).

## Attributed asset round (2026-10-29)

The new `cyberpunk2077.game-assets.en` pack adds **16 unique icons**, **146 item-record descriptions
(16 distinct in-game captions)**, **193 official perk descriptions**, and **one Night City background**.
Its 92 modern cyberware IDs supplement the 232 baseline classified IDs, with catalog-only status
explicitly retained. **Total new image bytes: 584,946** (27,826 icons + 557,120 map); the complete new pack is
**710,750 bytes** including text, manifest and provenance sidecars. Text is minified
JSON. Every icon-bearing catalog record resolves through F2; inventory fields browse `item-art`
when enriched. Equipment/cyberware rows show image and caption; catalog inspection actions include
both `item-art` and `perks`. A selected local item pack owns presentation for its matching records.
The host validates and caches embedded packs once; documents pin immutable shared images. Plugin
code performs no file/network I/O and builds image cells only for requested rows; map lookup happens
on lazy map expansion. The host's embedded loader is eager, so pack bytes are loaded at startup.

The [source sidecar](SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks/cyberpunk2077.game-assets.en/data.source.md),
[upstream ledger](SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks/cyberpunk2077.game-assets.en/upstream.json),
manifest, Sources panel, image-cell captions and `Info.References` retain full attribution.
`reference-data/generate-assets.py` rebuilds offline from reviewed downloads in `%TEMP%`; Pillow
header validation and image processing happen outside the repository. No downloaded code executes.

Sample verification: **152 passed / 0 failed / 0 skipped**; API build: **0 warnings / 0 errors**.
Matched fresh-process median first load (packs + largest sample + root): **372.56 → 370.18 ms**,
allocated bytes **136,135,608 → 140,845,168**. Timing is noisy; no speedup claim. See
[ASSET-VALIDATION.md](ASSET-VALIDATION.md) for raw timings, methodology, cache behavior and sizes.

## Malformed-input robustness review (#40)

The plugin rejects malformed CSAV containers, directories and required known records with a
contextual `SaveFormatException` (the SDK's documented format rejection). Optional read-only
inspection readers retain their existing warning/raw-data fallback for unsupported or malformed
inner schemas. Existing limits remain **128 MiB input, 64 MiB total expanded payload, 4,096 chunks,
100,000 nodes, 128 nested parent levels and 1,024 ASCII bytes per node name**. Counts and ranges
are checked before their corresponding allocations or traversal; these are editor budgets.

Fixed: a declared child could point to the following sibling or inherited parent boundary, leaving
an accepted empty branch whose child list contradicted the directory. `CsavTree` now rejects that
link before descending. Both authored corruption cases failed before the fix; valid direct and
nested controls retain exact tree relationships and byte-identical no-op output.

Added **85 cases**: 77 synthetic cases and eight sample mutation cases (each visits every supplied
gameplay snapshot/backup). Coverage includes multi-chunk overlaps, invalid/overflowing stored and
expanded lengths, cumulative expansion rejection before decoding, truncated wrappers/LZ4 grammar,
chunk/directory truncations with intact footers, node IDs/ranges/links, complete-table count/depth
boundaries, packed integer/name/fact errors and deterministic byte mutations. Sample payloads are
mutated and independently framed in memory; private files are neither changed nor copied into the
repository. Accepted controls preserve original bytes. No elapsed-time assertions or sleeps.

Research rechecked the pinned WolvenKit
[CSAV reader](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/IO/CyberpunkSaveReader.cs),
[RED package reader](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/RedPackageReader.File.cs)
and [persistency reader/writer](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PersistencySystem2Parser.cs)
(WolvenKit contributors, GPL-3.0; format research only, no code copied). The garage's outer class
may end at its byte boundary without a terminator; this is intentional, while nested vehicle
terminators remain required. The [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md)
(LZ4 contributors, format research) also confirms that the final literal sequence ignores its match
nibble; a positive control retains this accepted case. Unknown gaps/directory extensions stay opaque.

Validation: baseline **173 passed / 0 failed / 0 skipped**; final Release suite **258/0/0**;
sample-free suite **149 passed / 0 failed / 109 expected sample skips**. All 77 new synthetic cases
run without samples. Full Release API build with warnings as errors: **0 warnings / 0 errors**.
TRX/build evidence: `D:/Temp/saveeditor/reviews/review-robust-cyberpunk2077/`.
Experimental maturity, write warnings, no-op preservation and existing edit/history coverage remain
unchanged. No confirmed findings deferred; changes stay inside this plugin folder.

## Test quality review (#40)

Fact bindings live in queryable collections, so eager/lazy view traversal alone cannot test their
editability. Sample assertions now load actual fact rows, check their Unknown confidence and write
warnings, and require six proficiency fields. Unsupported schemas must retain the raw explorer while
queried facts reject edits. Shared sample discovery requires gameplay files after loading an archive;
missing archives still skip through `SampleSaves.Load`.

Independent modifier fixtures cover constant, combined and curve records, mixed-record alignment,
nonfinite values, unknown classes, suffixes and the 4,096/4,097-record boundary. Every truncated record
is framed with a repaired outer length so the wrapper cannot mask its rejection. Corruption cases
start from fresh valid bytes and assert contextual diagnostics. A shared synthetic inventory fixture
lets the 1,000-row materialization check and 100/101/128-row presentation boundaries run without
private samples. No existing corruption coverage was removed and no production changes were needed.

Release validation: **286 passed / 0 failed / 0 skipped** with samples; with `SAVETOOLKIT_ARTIFACTS`
unset, **181 passed / 0 failed / 105 expected missing-sample skips**. API Release build with warnings
as errors: **0 warnings / 0 errors**. Stopwatches only report measurements; there are no wall-clock
deadlines. Plugin fixtures use memory streams and byte arrays; shared `SampleSaves` owns extraction
and process-exit cleanup on the configured D: scratch root.

## Maintainability review (#40)

CSAV parsing now separates chunk validation from decompression, and directory parsing from tree
assembly. Export separates leaf replacement, chunk output and final verification. Progression has
named readers for player handles, ownership, balances, mirrors, attributes and proficiencies, separate
array encoders, and separate binding builders. Inventory, RED pools, forced modifiers, evidence and
inspection rows likewise use small named phases. Production methods and constructors are at most
50 lines; all handwritten C# files remain below 300 lines. Validation order and catches are retained.

The sibling-range tests now reuse the independent shared CSAV encoder. Four byte fingerprints
confirm that both valid and overlapping fixtures remain identical. Seven writer characterizations
cover nested-leaf growth/shrinkage across compressed and raw chunks, unchanged-chunk reuse and
exact replacement errors. Two progression cases cover repeated view builds, mixed edits/reopen,
invalid-mirror warnings and exact restoration. A complete root/collection-page/evidence fingerprint
also passes against the original pre-review production sources, not only the refactored sources.

Release validation: **300 passed / 0 failed / 0 skipped**, including all 85 robustness cases, exact
diagnostic transcript fingerprints and byte-identical sample no-op round trips. With samples unset:
**192 passed / 0 failed / 108 expected missing-sample skips**. API Release build with warnings as
errors: **0 warnings / 0 errors**. All production quoted strings are retained unchanged; public
behaviour, IDs, confidence labels, warnings and Experimental maturity remain unchanged. No findings
deferred. Evidence: `D:/Temp/saveeditor/reviews/review-maint-cyberpunk2077/`.

## SDK helper migration (#40)

`InspectionCollection` now adapts the shared `InMemoryCollection` engine. Cheap live text
projections, exact Int128 numeric comparisons with ordinal empty-value fallback, case-insensitive
text comparisons, cached native row identities and the original query diagnostics remain local.
The shared engine owns search, filters, stable-key tie breaks, paging and F14 counts. Rows without
empty-row presentation metadata retain their original visibility and empty markers. The nullable
sorting fixture now separates its nonempty native keys from empty display values, as real rows do.

`BinaryData` delegates bounds and little-endian primitive reads to `BoundedBinaryCursor`. Its thin
range adapter preserves the original `SaveFormatException` message and absolute offset instead of
exposing the SDK's different diagnostic. CSAV packed integers/names, limits, layout, LZ4 chunking
and writer policy remain local. No format or write-safety policy changed.

Before either migration, the baseline passed **258 tests, zero skipped**. Ten characterization
cases pin exact exception types/messages over deterministic mutations and truncated ranges,
plus query validation, numeric/text ties, canonical whitespace and empty projections. Existing
packed-value and invalid-child-link tests now assert complete diagnostics. After both migrations,
the plugin suite passes **268 tests, zero skipped**, including all 85 robustness cases and
byte-identical sample no-op round trips. API build: **zero warnings / zero errors**.
Production helper code is **38 lines smaller**.

## Framework feature adoption

Performance review (#40): compact merged fact identities and lazy stable-key paging reduce
largest-sample warm open + first-root allocations **61,416,304 → 26,168,104 bytes** and retained
managed document memory **42,454,264 → 19,971,640 bytes**. Ten 50-row fact pages allocate
**27,364,520 → 2,669,384 bytes**. All root/collection/lazy views match the baseline, with no-op
bytes preserved. **172 passed / 0 failed / 0 skipped**. See [PERFORMANCE-REVIEW.md](PERFORMANCE-REVIEW.md)
for matched timings, D: benchmark artifacts, regression failures and measurement limits.

| Feature | Adoption | Where / concrete scope |
| --- | --- | --- |
| F1 SaveDiff | Used | Hash-pinned `Evidence/corpus.json`, `CyberpunkEvidenceDecoder`, independent semantic/byte-diff edit tests and final ZIP/backup preservation. The native decoder is registered through the F12 SDK provider. |
| F2 reference packs | Used | Embedded MIT identifiers, CC BY-SA labels/captions, CDPR item/cyberware icons and official perk text, plus Piggyback/CDPR map art; immutable `IReferenceContext`, per-dataset attribution, local-pack precedence and raw fallback. |
| F3 rich cells | Used | Attributed inventory icon cells/details and equipment/cyberware image cells with text alternatives, confidence badges, raw-byte sections and stack actions. Missing art stays an explicit placeholder. |
| F4 reference fields | Used | `ReferenceSpec` inventory base-record inspection fields and selectors for every available reference catalog. Read-only IDs use the shared browse-only F13 picker; no unsafe identity-replacement setter. |
| F5 structural undo | N/A | No complete Cyberpunk add/remove/reorder writer exists: node links/counts, duplicate prefixes, unique counters and dependent stat/effect initialization cannot be repaired from this corpus. Scalar name/property materialization already undoes via values and preserves original bytes; it does not create inventory/perk records. |
| F6 planned actions | Used | Set-supported-stack-target and refill-to-opening-quantity previews, exclusions/warnings, document/explicit/empty/F8 whole-query scopes (#27), atomic apply and one undo group. No coherent perk-allocation preset exists without effect/requirement mutation. |
| F7 document groups | N/A | Only `sav.dat` is authoritative and owned here. No demonstrated coupled write to `user.gls`, settings or metadata; JSON partners belong to GenericJson. Core already mirrors gameplay backups and the ZIP test verifies that behavior. |
| F8 collections | Used | Complete inventory, saved/merged facts, equipment, cyberware, development, modifier, garage/travel and journal queries; max 200 materialized rows. Spatial views use separate lazy 128-entry pages. |
| F9 maps/graphs | Used | Completion round: `PerkGraphInspection` and `TravelMapInspection`: stable IDs, accessible text alternatives, bounded pages, independently expandable field targets, local attributed Night City background, unpositioned marker list until a save-ID crosswalk is verified, official reference perk text and no invented prerequisite edges. |
| F10 exact/nullable/union values | Used | Completion round: merged fact-value details use read-only `NullableSpec(UInt32)`: null for absence, integer zero for saved zero; live getters follow edit/undo. Numeric sorting unwraps nullable integers. Exact JSON lexemes and union alternatives are N/A to the fixed binary integer/float schema; UInt64 identities already use lossless decimal strings. |
| F11 shared codecs | N/A | CSAV uses raw LZ4, already handled by managed K4os with exact-length checks and original-chunk preservation. Shared F11 provides Kraken/SQLite, neither occurs in this format; no local Kraken/SQLite implementation exists to replace. |
| F12 decoder hooks / F8 diff traversal | Used | F12/F13 follow-up: production SDK-only `CyberpunkEvidenceDecoder` registered by `ISaveDecoderProvider` as `cyberpunk2077-evidence`; tests use shared `HookSaveDecoder`. The shared view decoder also traverses F8 collections. |
| F13 non-mutating inspection / browse-only picker | Used | F12/F13 follow-up: all seven catalogue inspection actions publish `Mutates = false`, including on read-only schemas. Existing read-only `ReferenceSpec` fields use shared browse-only search/category/sort/details. Float durations are N/A: exposed floats are forced level/attribute stats, not time values. |
| F14 empty slots / one table per list | Used | F14 audit (2026-10-29): one inventory table (eager through 100 rows, F8 above), one F8 attachment table per item, and F8 equipment slots with explicit vacancy flags, matching hidden counts and default hiding. Original node/slot/part keys, raw bytes, edit/undo and whole-query visibility remain intact. |

## F14 table and empty-slot audit (2026-10-29)

Inventory no longer offers linked eager pages: up to 100 entries appear in one lazy-loaded table;
larger collections use the existing complete-model F8 search/filter/sort provider. Attachment/part
trees likewise use one F8 table per item instead of linked 128-row tables. Their existing
`item-parts/<node>/<path>` row keys remain unchanged; collection metadata is registered when the
tree is expanded. Unknown/zero attachment IDs remain visible because no vacant-part rule is proven.

The owner-1 equipment table now marks only an actual stored slot with no item identity as empty.
The existing reader recognizes an omitted `itemID` or a zero `gameItemID.id` in that stored slot;
omitted slot arrays retain their diagnostic row (`Index == -1`) and are **not** vacant slots.
Shown vacant rows say **Empty slot**. Occupied slots with unknown IDs or unresolved inventory
joins remain visible. Both collection metadata and its F8 shell publish the same default-hiding
settings. Text/column filters run before counting vacancies; visibility runs before sorting and
paging. `TotalCount` retains all rows, `FilteredCount` counts matching visible rows, and
`HiddenEmptyCount` counts matching vacancies excluded only by visibility. Explicit `IncludeEmpty`
also governs F6 whole-query selection, including exclusions; stack actions still reject equipment
scopes because equipment inspection is read-only.

All remaining tables were audited: saved/merged facts, cyberware inventory/catalog, development,
item modifiers, garage/travel and journal already use one F8 table per collection. Their absent
catalog entries, zero values and diagnostic records are meaningful, not vacant slots. Inventory
itself is a counted item list rather than a fixed slot array; unknown IDs and zero quantities do
not establish vacancy. The single compression-chunk table contains actual physical chunks and
has no empty rows. Raw node-tree and spatial pages are bounded explorers, not split inventory tables.
No inventory or attachment next-page sections, or “Slots N–M” groups, remain.

Research rechecked the pinned WolvenKit
[gameSEquipSlot](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/gameSEquipSlot.cs)
and [gameItemID](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/gameItemID.cs)
schemas (WolvenKit contributors, GPL-3.0; read as format research, no implementation copied).
Sample tests independently compare every equipment row/identity with the decoded package and
preserve byte-identical no-op output; no inference about unlocked slots or available capacity is added.

Regression coverage includes matching hidden counts, omitted/default/explicit visibility,
filter-before-paging, stable keys, unresolved occupied equipment, zero/unknown inventory IDs,
F6 exclusions, the 100/101-row inventory boundary, a 141-part query across all pages, sample part
publication, and visible quantity edits changing only the original four payload bytes with exact undo.

Validation: **167 passed / 0 failed / 0 skipped** with the external private samples; API build:
**0 warnings / 0 errors**. All plugin C# files remain below 300 lines. No files outside this plugin
were changed, and no sample data was copied into the repository.

## F12/F13 follow-up

The production plugin exposes the existing native evidence paths through the SDK-only F12 hook;
no Testing dependency or plugin-local decoder registry is required. Select it with
`savediff compare before.zip after.zip --decoder cyberpunk2077-evidence`, or discover it through
`savediff list-decoders`. Header values, full-identity inventory quantities/body fingerprints and
all saved fact planes retain their previous paths. File/value/depth limits bound decoding; the
host reports incomplete output. Fact-table node IDs remain current-tree scopes, not proven
persistent cross-save identities.

All available nonempty reference catalogues retain their inspection action when the document is
read-only. F13 executes these message-only actions on an isolated copy with pinned references;
no values, serialized bytes, revision, dirty state or undo/redo history change. Existing read-only
reference fields use the framework's browse-only picker. Category filtering and declared Key/Name/
Category sorting are provided by the shared F2/F4 catalogue, with no duplicate plugin metadata index.
The saved/catalogue union indexes remain necessary for ownership, absence and native-instance joins.
Float duration hints are N/A: published floats are forced level/attribute stats, not durations.
The `63580dc` F6 reference-parameter fix is N/A: stack plans use an integer quantity or no parameters,
with no enum fallback or reference-based replacement action.

Validation with external private samples: **145 passed, 0 failed, 0 skipped**; API build:
**0 warnings / 0 errors**. Tests cover SDK hook discovery, foreign-file refusal, deterministic evidence,
file/value/depth bounds, all seven catalogue actions across three read-only version refusals, and
unchanged bytes/revision/dirty/undo/redo state on clean, edited and undone documents.

## Stored-perk graphs and travel maps

The **Skills and perks** tab publishes lazy stored-modern-perk diagrams. The supplied save has six
groups and no allocated modern perks; the diagrams show those group summaries, rather than a
fabricated full tree. Populated stored ranks are supported by independent synthetic tests. Labels
remain exact RED identifiers; all states are **Unknown** because the save alone cannot establish
eligibility, active effects, rank limits, point costs or Relic entitlement. No edges are emitted:
group membership is not a prerequisite. Legacy perk areas remain in the F8 table, separate from
modern allocations. Point budgets continue to use live F8 values; no graph budget is cached.

The **World** tab publishes the same full travel catalog/saved union as lazy map-marker pages.
**Found means registered in saved FastTravelSystem**, not discovered, accessible or completed.
Unregistered catalog entries and unavailable decoding are **Unknown**, never Missing/locked.
Without a pack the saved point still appears by its full raw ID. Exact field targets work without
querying the F8 table. All entries remain available in the browser's accessible text list.

The travel map embeds a local **1,792×1,792 WebP** assembled from 49 public zoom-3 tiles of
[Piggyback's official Night City map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city).
Piggyback Interactive Limited / CD PROJEKT RED retain rights; use follows the owner decision of
2026-10-29 with attribution. Tile placement is native top-left-origin image space. **No verified
saved travel-ID/geographic crosswalk exists**, so all saved markers remain unpositioned in the text
list. Bounds 0..1 are display extent only, not a world-coordinate transform. The background is
reference art and may target another build. Missing packs omit the image and retain IDs/status/list.
No runtime network fetching occurs; the source link opens only on click. The official CDPR planner
supplies reference perk names/descriptions, joined by exact modern perk enum, while stored ranks,
Unknown eligibility/effects, technical identity, navigation and absent prerequisite edges are preserved.
Neither the map nor the planner text claims PS5 2.21 gameplay rules. See the adjacent
[spatial source ledger](SaveToolkit.Plugins.Cyberpunk2077/SpatialViews.source.md).

All diagrams contain at most 128 entries per lazy page, with stable identities independent of array
order or labels. Graph IDs hash the complete record kind/group/technical name so even maximum-size
RED names fit the F9 ID limit. Duplicate identities omit the graph with a reason, while retaining the
raw table. Oversized spatial prose points to the full provenance in the linked detail field.

**Write-enable round:** the owner's decision is “Enable writes. Keep warnings.” Raw stored values are
now editable without waiting for console evidence. This does not implement a level-up transaction or
quest-completion recipe. The sample omits `expToLevel`, `maxLevel` and `currentExp`; no PS5 2.21 XP
curve, reward/unlock rules or opaque integrity checks are verified. The game may reject, ignore or
reinterpret edited saves. Fields and action previews keep these warnings; validation is tracked in
[owner issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

## Enabled writes and their evidence

Nine new write capabilities ship: six progression scalars, existing raw saved-fact values, and two
planned actions. On the supplied save this exposes six progression fields and 2,750 fact values.
No fact records, inventory instances or perk allocations are created or deleted.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Stored Level | Inferred | Owner-1 `SProficiency.currentLevel`; sample level 1; pinned WolvenKit schema | Reparsed; matching forced stat updated; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): load/effect/re-save/reload, rewards/unlocks |
| Stored Street Cred | Inferred | Distinct StreetCred record, sample 1; same schema | Reparsed; matching forced stat updated when present; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): shown cred, rewards/unlocks, reload |
| Level current XP | Inferred | Zero-default `currentExp`, schema and independent inserted-property tests | RED names/properties/offsets repaired; reparsed; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): XP display and one threshold crossing |
| Street Cred current XP | Inferred | Zero-default `currentExp`, same schema/tests | RED names/properties/offsets repaired; reparsed; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): XP display and one cred threshold |
| Level stored XP threshold | Inferred | Zero-default `expToLevel`; schema, no proven curve | RED names/properties/offsets repaired; reparsed; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): whether game retains/recomputes threshold |
| Street Cred stored XP threshold | Inferred | Zero-default `expToLevel`; schema, no proven curve | RED names/properties/offsets repaired; reparsed; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): threshold and cred reward behavior |
| Existing raw fact values | Unknown | Validated packed count + uint32 value plane; source framing and exact payload tests | Four-byte patch only; hashes/order/count unchanged; opaque checks unknown | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one known fact, related stage/callback/forward play |
| Set supported stack quantities | Inferred | Existing quantity writer and 36-entry reviewed allowlist | F6 prevalidation; exact selected payload patches; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): quantities, charge behavior, re-save/reload |
| Refill stacks to opening quantities | Inferred | Original observed quantities in the opened save; never reduces stacks | F6 prevalidation; exact selected payload patches; reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): ammo/use/replenishment and reload |

Editable-field warnings and stack previews begin “Not validated on console. The game may reject or
overwrite the edited save.” Raw fact semantics remain Unknown. CSAV has no known
container checksum/signature, but integrity algorithms inside opaque data or console wrappers are
unknown, so **the game may reject the edited save**. The serializer re-decompresses and reparses
its output, including intended progression values and existing stat mirrors. No game keys or
credentials are needed or read.

Progression storage is signed Int32. Level/Street Cred policy ranges are 1..60 / 1..50; XP and
threshold policies are 0..100,000. Original values outside those ranges remain representable for
preservation/undo. These are editor bounds, not proven PS5 game rules; the corpus observes level/cred
1 and omitted XP/threshold zero. Raw facts stay within uint32 and the observed corpus maximum
(at least 1), preserving every original value. Unknown signed fact meanings are not reinterpreted.
An unreadable forced-stat buffer or an existing conflicting level mirror refuses that level setter
instead of guessing which copy is authoritative; XP/threshold setters remain independent.

Both stack actions use F6 preview/apply with named old/new quantities, skipped rows and integrity/
console warnings; apply is one undo step. Money and unsupported structures are excluded. Null
selection means document scope; an empty selection affects nothing. F8 query selections include
unloaded matches and explicit exclusions. Refill uses opening quantities rather than guessed game
caps; set-target retains the existing 999,999 editor policy. No catalog metadata broadens permission.

### Writes still disabled for structural safety

- Inventory split/merge/add/remove, replacement IDs, flags, attachments and equipped-item changes
  have no complete mutation path for duplicate prefixes, IDs, links, instance counters and dependent
  stat/effect references. Enabling a partial patch could leave dangling references or lose opaque data.
- Perk allocation/refund, build presets and cyberware changes lack the required effect-package,
  capacity and dependent-record mutation path. A raw rank/slot toggle would bypass those records.
- Garage/travel acquisition and quest presets lack creation/reference repair for their omitted arrays
  and callback/event state. Existing fact-value edits do not promise those operations.
- Other skills, legacy ranks, wider schema versions and `user.gls` have no prepared writer. No F5
  structural or F7 multi-file transaction was held back in #13; none is invented for these formats.

These limits concern missing structural/dependency writers, not waiting for owner validation.

## File roles and scope

- `sav.dat`: gameplay CSAV; claimed with matching title, filename and VASC/FZLC signatures.
- `settings.json`, `metadata.9.json`: owned by GenericJson; never claimed or rewritten here.
- `user.gls`: separate global/profile format; not claimed or modified.
- `screenshot.png` and system files: never output by this plugin. It returns only its gameplay
  `sav.dat`; the shared exporter may mirror changed gameplay files into console backup directories.
  Do not infer that those backups remain unchanged from the plugin's single-file output contract.

The plugin performs no filesystem/network I/O. All parsing and writing use the SDK's supplied bytes.

## Verified container layout

All integer words are little-endian. Magic strings show their on-disk byte order.

| Region | Layout |
| --- | --- |
| `0x00..0x18` | `VASC`, save version at 4, game version at 8, opaque clock/date/header bytes, archive version at 21 |
| `0x19` | `FZLC`, int32 chunk count |
| `0x21` | Descriptor triples: absolute file offset, stored size, expanded size (12 bytes per chunk) |
| First descriptor offset | Data begins after reserved table space; sample offset `0xC21` / 3105 |
| Compressed chunk | `4ZLX`, repeated int32 expanded size, raw LZ4 block; stored size includes the 8-byte wrapper |
| Uncompressed chunk | No wrapper; stored and expanded sizes must agree |
| Node directory | `EDON`, packed count, then packed ASCII name + next ID + child ID + virtual offset + size |
| Final 8 bytes | int32 directory **file** offset, `ENOD` |

Expanded node offsets use a virtual file with the same header/data start: subtract the first chunk's
file offset to index the concatenated payload. Each node starts with its int32 ID; parent sizes include
children. IDs, ordered ranges, containment, sibling chains, child links, chunk ranges, wrapper sizes
and exact LZ4 output lengths are checked.

Packed integers have six initial magnitude bits, sign/continuation bits, then seven magnitude bits per
subsequent byte. Names use negative byte lengths. Positive/UTF-16 name encoding is unsupported.
Unknown padding, gaps, directory extensions and opaque node bodies are retained.

Limits: 128 MiB input, 64 MiB total expanded data, 4096 chunks, 100,000 nodes, 128 nesting levels,
1024 bytes per node name. Invalid/oversized input raises `SaveFormatException`.

## Inventory, fields and confidence

The verified inventory schema has an int32 sub-inventory count after its node ID, then uint64 container
ID and int32 item count. Each item has matching 16-byte item-info prefixes before and inside its
`itemData` node: uint64 TweakDB ID, uint32 seed, byte structure, uint16 unique counter, byte flags.
After the node ID and prefix are byte flags, uint32 creation time and optional uint32 quantity.
Quantity is at node-relative offset 25. Extended item bodies are decoded read-only where their
entire bounded schema is understood; unrecognized bodies retain a warning and raw bytes.

The prefix length matters: old MIT CyberCAT uses a 15-byte variant. Its item parser cannot be applied
literally to this sample. Current WolvenKit's wire layout and the actual PS5 bytes agree on 16 bytes.

`Items.money` resolves to **0x0000000BF5E188EC**: CRC32/IEEE of its UTF-8 name in the low word, name
length 11 in the next byte. The sample's unique container-1 money item is node 39, quantity **300**.
This meaning is **Verified** by the hash match, structure and upstream money editor.

The inventory resolves labels through the pinned `items` F2 catalog. The embedded identifier pack
contains 5,248 CRC32/UTF-8-length-verified names joined to community class IDs, with no localized
descriptions/icons/limits. Optional separately attributed local packs can supply richer labels.
Without a matching pack, the **36-entry reviewed dictionary** in `Resources/ItemNames.json` remains
the fallback. That original allowlist alone gates quantity writes; catalog categories and limits
cannot widen them. Unknown names display their complete 64-bit hash. Each label/category/details
cell carries provenance. Stable row keys remain original node IDs for this nonstructural document;
the full 16-byte saved identity is shown separately, so identical base records remain distinct.

| Sample node | Verified record | Quantity |
| --- | --- | --- |
| 44 | `Items.FirstAidWhiffV0` | 1 |
| 45 | `Items.BonesMcCoy70V0` | 1 |
| 49 | `Ammo.HandgunAmmo` | 600 |
| 50 | `Ammo.RifleAmmo` | 600 |
| 51 | `Ammo.ShotgunAmmo` | 120 |

Only container-1, structure-0, exactly 29-byte quantity-only records with a dictionary category of
Ammo, Component or Consumable are additionally writable. Recipe records, player body placeholders,
unknown hashes, other containers and extended items stay read-only. Crafting components and sniper
ammo are dictionary/source verified and exercised using synthetic item identities in the sample's
quantity-only layout; **the supplied save does not contain those records**. Consumable quantity bytes
are verified; the game's 2.x charge/replenishment behavior after loading is untested.

`FactsTable` contains packed count, a uint32 hash plane, then a uint32 value plane. Count and node
size must agree exactly. The F2 fact catalog verifies exact FNV-1a/32 canonical names, including nine
whitespace-sensitive strings. Hash mismatches retain raw IDs; duplicate catalog keys are rejected.
Names have Inferred semantic confidence: a debug/absent/named fact does not establish completion.
Existing raw values are editable in Saved facts with Unknown confidence and explicit warnings;
catalog-only facts stay absent. No fact insertion, hash changes or quest-unlock presets are offered.

## Read-only equipment, extended items and world state

`ExtendedItemReader` independently decodes the researched additional-info and recursive slot-part
wire grammar with exact-end checks, at most 512 parts, depth 12 and bounded UTF-8/UTF-16 strings.
The sample contains **10 extended items**, including the cutscene Unity's three child parts.
Inspect raw quest/not-unequippable flag bits, seed/counter/flags, creation time, loot-pool ID,
appearance, attachment-slot IDs and opaque uint32 members. The stored required-level float uses
`float.MaxValue` in this sample; it is explicitly not presented as the displayed tier or legal level.
Attachment tables page at 128 rows. Unknown suffixes disable decoding, preserve bytes and show a reason.

The bounded scriptable reader follows `EquipmentSystem.ownerData` to the unique owner-1 record.
It reads `gameSLoadout` areas/slots and joins the complete gameplay identity: TweakDBID, seed,
unique counter and flags, in player container 1. Structure is a wire property, not a gameItemID member.
The sample has **36 areas, seven occupied slots, six exact inventory matches**; special `RightArm`
remains unresolved. Duplicate base IDs never resolve by row position. Wardrobe/fists/cutscene slots
are shown as stored entries without claiming current usability. All sampled cyberware slots are empty.
The separate cyberware union browses all 232 classified catalog records alongside owned instances
and unresolved slot identities. This sample has catalog-only rows, no classified owned implants;
unknown inventory records are never silently excluded from the general inventory explorer.
Capacity/cost, bonus contributors, prerequisite evaluation and compatible replacement rules remain
Unknown; an empty slot is not proof of unlocked or eligible cyberware capacity.

Stats inspect the `StatsSystem` key/value arrays and the researched ItemID hash of record/seed/counter,
separately for each inventory instance and decoded part. Exact unique keys expose constant, combined
and curve modifier bodies; missing/ambiguous keys and unknown modifier classes stay explicit. The
sample has **48 diagnostic/modifier rows**; child parts have no saved stats-key match. Stat hashes,
operations, curve names and raw values do not establish computed final stats, tier or capacity.

Garage inspection walks **53,742** bounded persistency entries by their declared lengths and selects
the unique `vehicleGarageComponentPS` hash. Typed hashed fields support active, unlocked and favorite
vehicle ID lists; unfamiliar properties disable garage status rather than scanning through opaque data.
This save stores three empty active slots and omits unlock arrays. The 105-row catalog consequently
says **not listed in decoded garage**, rather than claiming unavailable/locked. Nonempty unlocks are
synthetic format coverage only; owner acquisition/summon captures remain required.

Fast travel follows typed handles from `FastTravelSystem.fastTravelNodes`, preserving record IDs and
marker refs separately. One saved point is registered: `FastTravelPoints.hey_gle_dataterm_04`, marker
`#hey_gle_dataterm_04`. The 232-row catalog distinguishes registered from not registered; current map
locks, discovery cause, DLC entitlement and accessibility remain Unknown. No unlock actions or map
coordinates are invented. Unknown saved records remain in both world unions with their full IDs.

The F8 merged-facts view has 2,750 saved rows (including 1,621 unknown hashes) plus 18,228 catalog-only
rows. Every absent row has **no stored value**, distinct from saved zero. Prefix groups are identifiers,
not quest titles; no completion percentage or stage inference is offered. Journal diagnostics browse
594 opaque 15-byte records, retain two other planes and a 16-byte suffix, and label the tracked-path
candidate Unknown. Journal records, facts and mappins are not equated with each other.

Merged fact-value details now expose that distinction through F10: absent rows contain typed null,
saved rows contain an exact uint32, including zero. These are read-only projections of the existing
saved-fact editor, not new fact records or additional writes. Queries and bound detail values follow
raw edits, undo and redo. Presence/exact-empty filters remain available and numeric sorts remain numeric.

## Player development and dependent stats

`ScriptableSystemsContainer` is an int32 node ID, int32 package length, then a RED package. The verified
package starts with byte version 4, byte flags 0, uint16 section count 6, uint32 component count,
four uint32 relative pool offsets, uint32 root-ID count, and uint64 root IDs. Offsets are relative to
the byte after that root-ID list. Name descriptors hold a 24-bit offset and 8-bit null-terminated
ASCII size. Chunk descriptors hold a uint32 type-name index and uint32 relative offset.

Each class starts with uint16 field count and `(uint16 name index, uint16 type index, uint32 offset)`
headers. Field offsets are relative to the class start. Struct arrays start with int32 count;
primitive members determine each record's end. Only the specifically needed flat struct types are
traversed. Unknown package chunks and unknown outer property bodies stay opaque.

The plugin follows `PlayerDevelopmentSystem.playerData`'s single handle, requires the referenced
`PlayerDevelopmentData` type and `ownerID.hash == 1`, and rejects ambiguous records. Zero-valued
primitive properties can be omitted. The cited enum defines missing development-point `type` as
`Attribute` (0); `Espionage` means relic points and `Primary` means perk points, as mapped by the
upstream player-stats editor.

Sample-verified development values: **level 1, street cred 1, both current XP 0, all five base
attributes 3, unspent attribute points 7, perk/relic points 0**. The zero perk/relic `unspent`
properties are absent. Their edits add one 8-byte property header and one 4-byte Int32 per record.
Spent balances, learned perks, secondary points and opaque data remain unchanged. Changing an
attribute does not consume/refund points or grant/remove perks; this matches the upstream editor's
independent attribute and point controls.

Attributes map Body to `Strength`, plus `Reflexes`, `TechnicalAbility`, `Intelligence`, and `Cool`.
Each edit also patches its unique additive `gameConstantStatModifier` in the entity-1 `StatsSystem`
`forcedModifiersBuffer`. FNV-1a/64 hashes of the stat names identify these entries. Both original
values must agree; missing, duplicate, nonconstant or mismatched mirrors disable the affected
attribute edits with a reason. Every changed mirror is reported as a derived `ValueChange`, exposed
as a read-only field, and recomputed through framework undo/redo. These meanings and byte layouts
are **Verified** against the cited sources and both supplied gameplay entries. In-game acceptance,
undocumented dependencies and behavior of extreme values remain **Unknown**.

## Writing, compression and integrity

Money, named stack quantities and attribute/stat pairs use fixed-size patches. The money cap is
100,000,000; other stack caps are 999,999; point-balance caps are 100,000. Existing larger values stay
representable. These are editor policies, **not verified engine limits**. Attributes use the upstream
3..20 range, extended to retain an existing out-of-range value. Values are validated atomically.

Point/progression edits rebuild only their affected records/arrays, containing player class and
package descriptors. Existing opaque fields/chunks, root IDs and class padding are retained.
Omitted XP/threshold names are appended without renumbering existing name indices; all original
name bytes/padding and chunk bodies remain intact. Package length, containing node size, subsequent virtual node offsets, chunk
expanded/stored lengths, chunk file offsets and the final directory pointer are repaired. No items,
chunks or CSAV nodes are inserted. Node IDs and links are unchanged.

No-op serialization returns the original bytes, including after undo. Changed chunks are re-encoded
with K4os raw LZ4. Unchanged compressed chunks, reserved bytes and inter-chunk gaps are retained.
Fixed-size patches retain expanded chunk boundaries; growth is assigned at the end of the replaced
node. Serialization re-decompresses and compares the complete intended payload, revalidates the
node tree/inventory/facts, re-parses progression and checks edited points/attributes/proficiencies and stat mirrors.
The raw payload viewer shows fixed patches at original offsets; point/proficiency property and name
insertions materialize in the exported package. Live development/fact queries show staged values.

The supplied decrypted file needs no further encryption. The cited CSAV readers/writers define no
container checksum/signature; there is no known container checksum to recompute. TweakDB CRCs identify
names, not save integrity. Unknown internal bytes are preserved. Structural correctness and edit/reopen
preservation are tested; game acceptance and undocumented semantic dependencies remain untested.
Unknown opaque integrity algorithms may cause the game to reject edited saves; this warning appears
in writable field descriptions, action descriptions, previews and action outcomes. Maturity stays Experimental.

## Views and tests

Overview, player development, inventory, equipment/cyberware, item stats, skills/perks, world,
journal diagnostics, facts, reference catalogs, node tree and container tabs
provide attributed labels, confidence/warnings, instance details and paged original/payload/node
bytes. Inventories up to 128 rows use one lazy rich table; larger inventories and all facts use F8
whole-model search, exact column filters, numeric sorting and bounded pages (at most 200 rows).
Query cells are constructed only after filtering/paging. Chunk tables are bounded to 4096 rows.
Value IDs and row keys stay stable across edits; no structural inventory edits are implemented.
F4 pickers inspect technical catalogs and wiki labels without modifying saves. See REFERENCES.md for build/locale keys.

Sample evidence: **one primary record and its identical backup**, 1,302,721 stored bytes, 22 chunks,
5,607,178 expanded bytes, 159 nodes and 22 items. This is one distinct save, not a multi-session corpus.
Every supplied gameplay entry, including backups, is tested. Opening and initial views remain well
below the approximately two-second target on the development machine.

Tests cover conformance with a mixed money/ammo/attribute/point edit; individual round trips for all
five attributes, three point balances, three ammo stacks and two consumables; known sample values;
exact independent payload patches; preservation outside the resized node and of opaque package
chunks/properties; repaired node offsets and sizes; derived changes; framework undo/redo; byte-identical
no-op/undo; atomic rejection; unknown schema/package refusal; mismatched stat refusal; corrupt package
bounds; and independent package insertion/padding fixtures. Existing CSAV/LZ4 corruption, gap and
codec-vector tests remain. Hash vectors cover CRC32/TweakDBID and FNV-1a/64.

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
```

Validation for completion round: **141 passed, 0 failed, 0 skipped** with private samples; API build:
**0 warnings / 0 errors**. Adds F9 paging/stable identities/text alternatives/target publication,
link-first provenance, raw fallback and unsupported-schema checks; F10 null-versus-zero wire values,
read-only bindings, live undo/redo and numeric sorting; overlapping root/child-sibling refusal; and
ambiguous full-identity/stat-hash join regressions. New views preserve byte-identical original output
and leave the host session clean. Every C# file remains under 300 lines. No new dependencies or hooks.
Separate missing-sample check: **57 passed / 84 expected skips / 0 failed**; this checks absence
handling only and is separate from the sample-enabled 141/0/0 validation above.

Historical validation for write-enable round: **125 passed, 0 failed, 0 skipped** with private samples; API build:
**0 warnings / 0 errors**. Added coverage for all six progression setters, omitted-name insertion,
already-materialized scalar four-byte patches, unrelated opaque properties/chunks, mixed point/attribute/
proficiency growth, live queries, every nonempty saved fact plane, exact F1 byte ranges, invalid values,
F6 selection/exclusions/unloaded query matches/empty scope/stale plans, and one-step action undo/redo.

Historical roadmap round 2: **107 passed, 0 failed, 0 skipped** with private samples. With an empty sample directory:
**45 passed / 62 skipped**, zero failures (absence behavior only). API build: zero warnings/errors. Retained pack validity,
all-record hash verification, exact canonical whitespace, conflicting-ID rejection, pinned local
build/fallback tests, attributed sample joins, host F4 inspection/undo, live query edit/undo,
1,000-row paging, F1 evidence decoder and final ZIP/backup preservation tests. No hooks,
framework changes or additional NuGet dependencies were added. Round 2 adds full-identity equipment
joins and refusal, extended tree/encoding bounds, modern nested perk fixtures/defaults, typed garage
hash/terminator refusal, ItemID hash vectors, modifier-buffer refusal, merged-fact absence/whitespace,
complete bounded paging, wiki attribution/revision/hash joins, local-label precedence and live budget undo.

## Open problems

- Validate the enabled raw Level/Street Cred/XP/threshold edits on PS5 2.21; capture transition curves,
  rewards/unlocks and whether stored thresholds are retained or recomputed. Raw setters do not calculate these.
- Verify extended/unknown items, other schema/package versions, other regions and later-game samples.
- Validate component records in an actual PS5 sample, consumable charge behavior and all new edits
  by loading on a console. Establish separate global/profile integrity before editing `user.gls`.
- Modded/missing/nonconstant player stat buffers are intentionally unsupported for attribute writes.

## Sources and attribution

Technical dictionary data is factual identifiers/categories from the pinned MIT community source.
The separate wiki-label dataset remains **CC BY-SA 3.0**, with revision links and content hashes.
Publisher icons, captions and planner text are now bundled separately with the owner-decision notice below;
community-derived captions/ID joins remain CC BY-SA 3.0. No GPL implementation is bundled.
Full per-dataset attribution is also beside the data, in `Info.References`, F2 manifests/Sources,
and every named field/details view. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

| Dataset / reference | Pinned source / author | Licence / retrieval / changes |
| --- | --- | --- |
| F2 `item-art`: 146 exact IDs / 16 captions | [Cyberpunk Wiki](https://cyberpunk.fandom.com/wiki/Cyberpunk_Wiki), contributors / CD PROJEKT RED; 16 revision-pinned pages and image URLs/hashes in [upstream.json](SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks/cyberpunk2077.game-assets.en/upstream.json) | CC BY-SA 3.0 for adapted text/ID joins; 2026-10-29. Infobox titles, explicit base IDs and in-game captions, markup removed. Base-record reference text/art only; tier effects and variant appearance unverified. |
| F2 16 inventory icons | CD PROJEKT RED; [Cyberpunk Wiki image uploaders](https://cyberpunk.fandom.com/wiki/Category:Cyberpunk_2077_images_-_Inventory_Icons), exact URLs/hashes in the ledger above | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29. Downscaled to ≤64×64, WebP, content deduplicated; **27,826 bytes**. Ten cyberware families plus six common item families; shared art represents the base family. |
| F2 193 official perk descriptions | [CD PROJEKT RED build planner](https://www.cyberpunk.net/en/build-planner), [content-hashed English chunk](https://www.cyberpunk.net/build/8603-b6e6f72c476c734a52a4.js) | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29. Text parsed as JSON, never executed; emphasis tags removed. Build coverage is unknown; no rules/writer changes. |
| F2/F9 Night City background | [Piggyback official map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city), Piggyback Interactive Limited / CD PROJEKT RED; 49 pinned tile hashes in the ledger | Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29. Native-index tile assembly, 1,792px WebP quality 72, **557,120 bytes**. No save-ID/position crosswalk; unpositioned list fallback retained. |
| F9 official Night City map link | [Piggyback official map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city), Piggyback Interactive Limited / CD PROJEKT RED | Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29. Background imported as described above; the source link, unknown geographic join and accessible marker list remain. Sidecar: `SpatialViews.source.md`. |
| F9 stored perk schema / external planner | [SNewPerk](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SNewPerk.cs), [SAttributeData](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SAttributeData.cs), WolvenKit contributors; [official planner](https://www.cyberpunk.net/en/build-planner), CD PROJEKT RED | GPL-3.0 schemas, format research only; no implementation copied. CDPR planner text is imported with attribution under the owner decision 2026-10-29. Diagram preserves exact saved identifiers/ranks; prerequisites and gameplay effect validation remain unknown. |
| Stored progression write schema | [WolvenKit SProficiency](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SProficiency.cs), WolvenKit contributors | GPL-3.0, rechecked 2026-10-28; format research only, independently written scalar/name-pool writer; no source/data copied. |
| Existing fact write framing | [PixelRick FactsTable](https://github.com/PixelRick/CyberpunkSaveEditor/blob/3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062/source/redx/csav/nodes/questSystem/FactsDB/FactsTable.h), PixelRick contributors | MIT, rechecked 2026-10-28; count/hash/value-plane research only; independent fixed patches retain order and all unknown hashes. |
| Baseline 36-name allowlist | [items.bin](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/items.bin), [ItemClasses.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/ItemClasses.json), DeweySalt / Deweh and contributors | [MIT](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/LICENSE), 2026-10-28; selected names/categories, verified hashes; preserved as write gate. Sidecar: `Resources/ItemNames.source.md`. |
| F2 `items` (5,248) | Same pinned items.bin + ItemClasses.json, DeweySalt / Deweh and contributors | MIT, 2026-10-28; exact hash join, retained baseline, decimal-string keys and coverage annotations; no gameplay limits. |
| F2 `facts` (19,357) | [Facts.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Facts.json), same authors | MIT, 2026-10-28; exact canonical strings retained separately, FNV-1a/32 verified, raw identifier category. |
| F2 `vehicles` (105) | [Vehicles.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Vehicles.json), same authors | MIT, 2026-10-28; computed TweakDB keys, identifier category; no garage join. |
| F2 `travel` (232) | [FastTravel.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/FastTravel.json), same authors | MIT, 2026-10-28; computed PointRecord IDs, retained MarkerRef/IsEP1; no coordinates/unlock claim. |
| F2 `item-labels` (15) | Cyberpunk Wiki contributors: [MaxDoc 297812](https://cyberpunk.fandom.com/wiki/MaxDoc?oldid=297812), [Bounce Back 297811](https://cyberpunk.fandom.com/wiki/Bounce_Back?oldid=297811), [F-GX 301113](https://cyberpunk.fandom.com/wiki/F-GX_Frag_Grenade?oldid=301113), [EMP 301118](https://cyberpunk.fandom.com/wiki/EMP_Grenade?oldid=301118), [X-22 287501](https://cyberpunk.fandom.com/wiki/X-22_Flashbang_Grenade?oldid=287501), [Unity 290048](https://cyberpunk.fandom.com/wiki/Constitutional_Arms_Unity?oldid=290048) | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), 2026-10-28; extracted titles and explicit base IDs only; exact hash/catalog intersection. No art/prose/limits or guessed variants. Derived dataset stays share-alike; `cyberpunk2077.wiki-labels.en/data.source.md` and `upstream.json` pin all source hashes. |
| Localization research only | [CDPR Modding Documentation: translation](https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/blob/main/modding-guides/everything-else/how-to-translate-a-mod.md) | Consulted 2026-10-28 for localization joins; no content/assets copied and no redistribution permission inferred. |

All four F2 datasets have `data.source.md`, upstream input SHA-256 ledger and normalized-LF content
hashes under `ReferencePacks/cyberpunk2077.identifiers/`. The original offline generator and schema
contract are described in REFERENCES.md. Existing parser research references follow.

Research/sample verification: 2026-10-28. Downloaded source was read as text in scratch storage;
no upstream application/script was executed. This is independently written format code. No GPL source
is copied or linked into the plugin. The managed K4os package is its only third-party runtime dependency. The trimmed reference dictionary
is attributed to CP2077SaveEditor (MIT); its notice is retained in [CP2077SaveEditor-LICENSE.txt](CP2077SaveEditor-LICENSE.txt).

- [CyberCAT at 50db326](https://github.com/WolvenKit/CyberCAT/tree/50db3263a94247531853ba13c2e200f2e784ba8d75/CyberCAT.Core):
  `Classes/CompressionHelper.cs` for FZLC/4ZLX and rebuilding, `Classes/SaveFile.cs` for directory/footer
  offsets and links, `ExtensionMethods.cs` for packed integers/names, `Classes/Parsers/FactsTableParser.cs`
  for fact planes. MIT; notice retained in [CyberCAT-LICENSE.txt](CyberCAT-LICENSE.txt).
- [WolvenKit at 65ea74b](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save):
  `Helper/InventoryHelper.cs`, `Classes/ItemData.cs`, `Classes/ItemInfo.cs` for current item wire facts;
  `IO/CyberpunkSaveReader.cs` / `CyberpunkSaveWriter.cs` for a second framing reference.
  GPL-3.0; format reference only, no source copied.
- [CP2077SaveEditor inventory control at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/InventoryControl.cs):
  confirms `Items.money` quantity in player container 1. MIT; no source copied.
- [CP2077SaveEditor player stats at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/PlayerStatsControl.cs):
  attribute names, Attribute/Primary/Espionage balance mapping and the dependent forced-stat updates;
  read together with [SaveFileHelper.cs](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Utils/SaveFileHelper.cs)
  for handle traversal and player owner/entity IDs. MIT; format/behavior reference, no source copied.
- [CP2077SaveEditor items.bin at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/items.bin):
  source of the trimmed embedded record-name dictionary. Names independently matched to CRC32/length
  hashes, including all 22 sample inventory identities. MIT notice retained. Also consulted
  [ItemClasses.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/ItemClasses.json)
  and [ItemDetails.cs](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/ItemDetails.cs)
  for item types and quantity editing. The dictionary records names; it does not claim every record
  appears or behaves identically in PS5 2.21.
- [WolvenKit package reader at 65ea74b](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/RedPackageReader.File.cs)
  and [RedPackageReader.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/RedPackageReader.cs):
  package pools, scriptable root-ID list, class properties, name indices and handles;
  [RedPackageStructs.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/Buffer/RedPackageStructs.cs)
  defines descriptor bit layouts. [PackageParser.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PackageParser.cs)
  supplies the int32 package-length wrapper. GPL-3.0; format reference only.
- [WolvenKit PlayerDevelopmentData.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/PlayerDevelopmentData.cs),
  [SProficiency.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SProficiency.cs),
  [SDevelopmentPoints.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SDevelopmentPoints.cs)
  and [cp77enums.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Enums/cp77enums.cs):
  progression field schemas and enum-zero defaults. GPL-3.0; format reference only.
- [WolvenKit ModifiersBufferReader.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/ModifiersBufferReader.cs)
  and [SaveHashHelper.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Helper/SaveHashHelper.cs):
  packed class name, FNV-1a stat hash, modifier kind and float value. GPL-3.0; format reference only.
- Read-only nested schemas at the same pinned WolvenKit revision: `EquipmentSystemPlayerData`,
  `gameSLoadout`, `gameSEquipArea`, `gameSEquipSlot`, `gameItemID`, `SAttributeData`, `SNewPerk`,
  `SPerkArea`, `SPerk`, `FastTravelSystem`, `gameFastTravelPointData`, `vehicleGarageComponentPS`,
  `vehicleGarageVehicleID`, `vehicleUnlockedVehicle`; [schema directory](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes).
  [PersistencySystem2Parser](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PersistencySystem2Parser.cs),
  [JournalManagerParser](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/JournalManagerParser.cs)
  and InventoryHelper were read as format research, independently implemented and checked against
  sample bytes/synthetic fixtures. GPL-3.0; no GPL implementation or generated source bundled.
- [PixelRick CyberpunkSaveEditor](https://github.com/PixelRick/CyberpunkSaveEditor): alternate tree/
  scriptable-system research lead; no parser copied or schemas inferred from its feature list.
- [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md): block grammar
  underlying literal and overlap vectors.
- [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4): managed raw codec,
  version 1.3.8 from the existing central package list, MIT.
- [Repository reconnaissance](../../docs/formats/PPSA04027.md): initial sample dimensions, independently
  rechecked against every supplied gameplay entry.

## SDK requests

No outstanding SDK requests. F12 [#32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32)
resolved bounded shared F8 traversal and native decoder registration; `CyberpunkEvidenceDecoder`
now lives in the production plugin and implements `ISaveDecoderHook`. F13
[#33](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/33) resolved read-only inspection:
catalogue actions publish `Mutates = false` and remain available when `CanEdit == false`.
Stack actions continue to use shared F6/F8 whole-query planning (#27).

## Licence exclusions

| Source / revision | Licence found | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- |
| [Piggyback Night City](https://maps.piggyback.com/cyberpunk-2077/maps/night-city), 49 public zoom-3 tile hashes pinned, 2026-10-29 | Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29 | **Resolved: imported (owner decision 2026-10-29)**, background art | Unpositioned marker list; positions remain omitted because a save-ID/geographic crosswalk is unverified. | No licence blocker remains. Positioning needs a verified save-ID/geographic crosswalk, independent of rights. |
| [CDPR planner](https://www.cyberpunk.net/en/build-planner), English chunk `8603-b6e6f72c476c734a52a4.js`, retrieved 2026-10-29 | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29 | **Resolved: imported (owner decision 2026-10-29)**, official names/descriptions | Reference text and stored ranks; no invented prerequisite edges or gameplay rules. | No licence blocker remains. Build-matched prerequisites/costs/effects/entitlement remain evidence gaps. |
| Complete PS5 2.21 TweakDB/localization/icon exports, no complete matching source supplied | Missing data; publisher assets are usable under owner decision 2026-10-29 | Partial import completed: 146 item IDs, 193 perks, 16 icons; no rights refusal | Remaining identifiers keep raw/baseline names and explicit missing-art fallback. No speculative joins. | Supply complete build-matched exports and verify exact identifier joins; this is an evidence gap, not a licence refusal. |
