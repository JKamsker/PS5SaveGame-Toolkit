# Atomic Heart (`atomic-heart`)

Experimental inventory editor and inspector for decrypted **PPSA10695**, SaveExtension tag/version 1/2,
package 522, engine 4.27.2. The supplied primary and backup are one identical
126,214-byte snapshot. The game patch, region, DLC and known in-game counts have
not been supplied. Broader versions are not validated.

The plugin uses `SaveToolkit.Sdk` and the shared managed Kraken decoder in
`SaveToolkit.Codecs`. It performs no filesystem/network I/O and loads no game DLL.
Unchanged compressed chunks and all unrelated decoded bytes are retained. No save
bytes, extracted records or private asset dumps are embedded. Attributed compact public game artwork is bundled as described below.

## Capabilities and confidence

- Decode every frame and verify exact per-chunk, aggregate and inner array lengths.
- Inspect SlotInfo and SlotData tags, manual subsystem/level/actor/component records,
  both class-path strings, tags, byte spans, native payloads and unknown tails.
- Search/filter/sort all 4,063 records through F8, with at most 200 rows per query.
  Open record details for bounded lazy properties and original hex.
- Query 90 serialized inventory slots through F8: resources, special resources, ammunition,
  auxiliary items, quest items and the player grid. F14 hides 79 confirmed vacant
  slots by default, leaving 11 populated rows; Show restores the empty rows and
  their raw read-only fields. One table covers the complete inventory with a container filter.
  Storage has no populated inspected slot records in this snapshot.
- F2 supplies 48 item references: the original 45 weapon/ammunition pairs plus two sample resources and a Small Neuromed Capsule. Twelve weapon labels now use official English names/excerpts. Twenty-three compact icons cover weapons, resources, modules and a consumable; separate read-only guide catalogues add six unmapped resources, two KS-23 modules and nine Shok reference costs.
  F3 details show descriptions, raw identifiers, provenance and original bytes.
  Unknown identifiers get explicitly authored formatting in details; inferred
  container/path categories never pretend to be localized game labels.
  F4 `ReferenceSpec`
  fields keep exact short asset IDs; unknown IDs stay raw. Existing populated
  references accept same-category catalog assets with unambiguous full object
  paths already present in the input. No path or DLC availability is invented.
- F4 also annotates catalog-matched `PrimaryAssetName` and `ItemDataAsset` fields
  in the read-only record explorer; raw full paths remain in their descriptions.
- F9 shows the three saved skill collections and two weapon references with
  **Unknown** states, no invented prerequisite edges or upgrade levels, and links
  to bounded evidence fields/hex. The sample skill sets/maps contain two zero
  counts each; that does not establish skill ownership semantics. Weapon labels
  update after reference edits and undo. Missing packs retain identifier fallbacks.
- F9's external-only location map lists all 262 saved levels, 99 per lazy page,
  with Unknown completion/discovery and links to the matching level rows and
  the community location guide. No geographical transform, map art, community
  coordinates, collectible crosswalk or fast-travel availability is asserted.
- F1 can walk over 9,100 exposed scalar values with `PluginViewDecoder`. Native
  state, opaque properties, transforms and velocities still require byte diffs.
  A synthetic relocated-property pair proves identity alignment and detection
  of a single changed value. The actual primary/backup have no differences.

**Verified** means the serialized name, type, value or structural boundary was
read from the bytes. It does not establish a gameplay effect, ownership, legal
maximum or PS5 write compatibility. Public PC labels joined by exact asset name
are **Inferred** on PS5. Unmapped references are **Unknown**. No numeric limits,
upgrade prerequisites, skill ownership or quest completion are invented.

Existing populated slots expose bounded Amount edits and the F6 action
`Set existing quantities (preview)`, with parameter `Target quantity (Amount)`.
Preview lists direct changes, skipped rows and warnings; selected rows
and whole-document scopes are supported. F8 select-all-matching scopes resolve
unloaded rows on the server and honor explicit exclusions and empty-slot visibility (#27, #38). An empty
selection or a query with no matches changes nothing.
Each apply is one undo group. Empty slots, raw metadata, record-explorer fields,
native data and instance IDs remain read-only.
Repeated same-name inventory containers or multiple `Slots`/`Elements` collections
within a container have no proven authoritative occurrence. They remain available
in the raw inspector and original hex, but are excluded from inventory write targets
and F6 rather than choosing an occurrence or merging their bounds.

`Serialize()` patches explicit payloads and enclosing byte counts, reuses unchanged
compressed chunks, emits changed chunks through `SaveToolkit.Codecs.EncodeStored`,
repairs framing/SHA-1, fully decodes/reparses the output and compares every plain
byte with the intended result. The framing writer calculates reuse and output
size before allocating one exact output buffer. One validated edited result is
cached per document; changed values invalidate it, and callers always receive a
fresh copy. Every serialization reparses outer framing/SHA-1. No-op serialization
(including undoing all edits) reuses the immutable decode already validated at
Open and returns a fresh byte-identical original, without decoding/recompression.
Original download and full-set export are already supported by the host.

## Enabled writes and their evidence

Owner decision, 2026-10-28: **Enable writes. Keep warnings.** These three write
paths are enabled now, with `Inferred` confidence and `Experimental` maturity.
They are not blocked on captures tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing populated slot `Amount` | Inferred | Explicit signed IntProperty/Int64Property tags. Values must be 0 through the original maximum in that container, bounded by storage width. Zero changes only the scalar; it does not remove a slot. | SHA-1 and byte counts repaired; output decoded/reparsed. Unknown inner integrity/duplicates and stored-block acceptance mean **the game may reject the edited save**. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small material/ammo/resource edit, observe/use/spend, save/restart/reload and diff. Observed bounds are not proven game caps. |
| Existing populated `ItemDataAsset` replacement | Inferred | Exact catalog join plus unambiguous full path already observed in this save, same catalog category. ItemId, Amount, grid position, native instance data and unknown bytes are retained. | FString encoding and affected property/struct-array/object/array lengths repaired; SHA-1 repaired; output decoded/reparsed. **The game may reject the save**, or dependent equipment/module/ownership state may not match. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one known weapon/ammo reference, inspect equipment/modules/capacity, use/equip, save/restart/reload. Category matching does not prove compatibility. |
| F6 `inventory/set-amount` | Inferred | Same existing Amount bindings; selected, document or server-resolved F8 query preview. Empty/unsupported slots, over-limit targets and unchanged values are listed as skipped. | Same repairs and rejection warning as individual amounts. Plans/outcomes retain warnings; stale/tampered plans fail; one undo/redo covers the batch. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): selected, filtered with exclusions and full eligible batches, compare intended and unrelated values after use/resave/reload. |

Writable fields, outcomes and action previews share the warning opening:
**Not validated on console. The game may reject or overwrite the edited save.**
The warning retains unverified stored Kraken block acceptance, inner integrity,
gameplay effects, ownership and safe limits, plus disposable-copy validation (#31).
Stored output can grow substantially; file/allocation limits are not console
slot-capacity guarantees.
No independent inner checksum is known. No guessed ownership/equipment repairs
are applied.

No prepared structural or multi-file action existed in the previous round.
Creation/removal/move/split, skill/loadout/progression presets and arbitrary raw
field writes remain unsupported because allocation, opaque instance state,
dependent references and event rewards cannot be safely reconstructed. Warning
text cannot prevent losing those unknown structures. This single-file document
does not declare unrelated partners; identical backup mirroring remains the host's
normal export behavior. Evidence is a validation request, not a gate on the three
enabled paths.

## Framework feature adoption

| Feature | Status | Adoption or concrete limit |
| --- | --- | --- |
| F1 SaveDiff | Used | `PluginViewDecoder` and the production `atomic-heart.saveextension-v1` F12 hook expose stable scalar tag identities; real twin/generation evidence and authored relocated-property fixtures remain in the evidence tests. |
| F2 reference packs | Used | Expanded in the 2026-10-29 asset round: host-loaded, hashed 48-entry item catalogue plus 8 screenshot resource/module and 9 Shok guide entries. Original GPL notices retained; game art/text and guide additions use the 2026-10-29 owner policy. Twenty-three cached icons; reference tables expand lazily. |
| F3 rich cells | Used | Added in the 2026-10-29 asset round: inventory details, descriptions/provenance/hex; existing action cells and field confidence badges. Inventory/query rows and reference tables show attributed F2 icons; missing images retain text fallbacks. |
| F4 reference pickers | Used | Expanded in the 2026-10-29 asset round: existing inventory asset replacements plus browse-only `ReferenceSpec` fields for read-only catalog-matched asset IDs in the record explorer and catalogue inspection action. Unknown IDs remain raw and never become invented choices. |
| F5 structural undo | N/A | No safe add/remove/move/split writer exists: opaque instance allocation, grid/indices, equipment and ownership references cannot be reconstructed from this snapshot. Scalar edits use normal undo. |
| F6 planned actions | Used | Query scopes added in the 2026-10-29 implementation round: `inventory/set-amount` previews selected, document and #27 F8 whole-query scopes with skipped rows, exclusions, warnings and grouped undo. Its only parameter is `Target quantity (Amount)` (key `amount`), so no item parameter needs a `ReferenceSpec`. Skill/loadout/spend/refund presets have no safe underlying writer. |
| F7 document groups | N/A | One self-contained gameplay file is rewritten. No coupled partner with a proved transaction contract exists; identical console backups are mirrored by the host. |
| F8 queryable tables | Used | All 4,063 records and all inventory slots; whole-model search/filter/sort, numeric Amount/ItemId ordering, stable IDs and bounded 200-row pages. Inventory details/hex live in the single F8 table; original raw record inspection remains available. |
| F9 maps/graphs | Used | Added in the 2026-10-29 implementation round: paged skill/weapon evidence graphs and external-only saved-level map, compact stable IDs, text alternatives, confidence and tested navigation. Prerequisites/coordinates/discovery remain unknown. |
| F10 exact/nullable/union values | N/A | The decoded UE tags use fixed-width signed/unsigned integers and IEEE floating-point scalars; no JSON numeric lexemes, nullable scalar storage or tagged union is rewritten. Existing Int128 SDK values preserve all integer bits. |
| F11 shared codecs | Used | Optimized in the 2026-10-29 implementation round: `SaveToolkit.Codecs` Kraken decode/stored encode; retained compressed frames, exact output allocation, validated serialization cache and byte-identical no-op. No local codec. |
| F12 SaveDiff F8 traversal/hooks | Used | Registered `atomic-heart.saveextension-v1`, a bounded SDK-only decoder for stable scalar tag paths; shared `PluginViewDecoder` now traverses the inventory F8 collection. |
| F13 inspect/browse/filter/duration | Used | Catalogue item inspection is `Mutates = false`; read-only asset references use browse-only pickers, and the pinned 48-entry item catalogue supports category filtering and declared category/key/name sorting. Duration hints are N/A because no timer field is published. |
| F14 inventory empties | Used | Added in the 2026-10-29 F14 round: one F8 inventory table replaces the F8/eager-page duplicate. Confirmed `None`/zero slots with ItemId `0` or `-1` are marked empty; shared metadata hides them by default. Complete-model matching precedes visibility, sorting and paging; hidden counts and F6 scopes retain every stable slot identity. |

## F14 table audit (2026-10-29)

The inventory previously appeared both as a searchable F8 table and as a second
99-row eager table with linked "More slots" pages. Only the F8 table remains;
its row details still contain the raw asset, amount, ItemId, provenance and hex.
The original slot keys, bindings and serialized order are unchanged. No inventory
"Slots N–M" sections or linked inventory pages remain.

The supplied snapshot has 79 rows with `ItemDataAsset = None` and `Amount = 0`:
10 have `ItemId = 0`, and 69 have `ItemId = -1`. Only that combined sentinel
pattern is hidden. Blank or unknown assets, other instance IDs and zero amounts
on populated assets are not assumed vacant. An Amount edit to zero therefore
leaves the populated asset visible, without inventing a structural removal.
Empty slots remain read-only; enabled edits cannot change the vacancy marker.
These observations describe this snapshot, not a broader game allocation rule.

The remaining tables are the F8 record index, raw decoded-level/chunk inspection,
and 48/8/9-entry reference catalogues. They contain records/reference facts rather
than inventory slots and have no confirmed vacancy sentinel to hide. Their raw
inspection navigation remains available, including the level-map targets.

Regression coverage checks default/explicit visibility, all 90 stable keys,
matching hidden counts before paging, a 450-slot mixed inventory, sentinel
counterexamples, shown raw values, query edits/round trips, and F6 query scopes
with both visibility choices, exclusions and undo/redo. The shared eager-only
conformance edit walk is replaced locally by an explicit F8 edit round trip;
the other shared conformance checks remain enabled.

Validation: **79 tests passed, 0 failed, 0 skipped** with the supplied samples;
the API build passed with **0 warnings and 0 errors**. All tracked changes stay
inside `plugins/AtomicHeart`; no sample data is committed.

## Completion audit against the plan

| Requirements / acceptance | Result / remaining owner work |
| --- | --- |
| AH-R1, AH-R2; exact decoding, integrity, spans and unknown retention | Shared codec/grammar/preservation implementation complete; serialization optimized and regression tested. Independent original compressed-Kraken agreement and unknown inner integrity/duplicate-state investigation remain validation items in #31. |
| AH-R3, AH-R5, AH-R18; known-state observations and console/patch coverage | Owner-blocked (#31): controlled captures, exact effects/bounds, independent states/builds/DLC and restore/use/resave/reload observations. No Stable claim. |
| AH-R4, AH-R8; catalog/reference enrichment | Imported official English weapon names/short excerpts and icons, two sample resource labels/icons, Small Neuromed Capsule effect/recipe, two screenshot KS-23 module costs and nine guide Shok names/costs. Unmapped native skill/module/resource IDs, full build-matched localization, caps, prerequisites and DLC rules remain evidence gaps. No exact localization or cap is inferred from a guide. |
| AH-R6, AH-R7 | Won't do: conditional native/proprietary codec infrastructure is unnecessary because F11's bounded managed route works. |
| AH-R9, AH-R10, AH-R13; pickers/rich cells/bulk review | Implemented with F2/F3/F4/F6/F8, including query scopes, live search, exclusions and group undo. Coupled equipment/skill presets await the owner-backed serialization/dependency model. |
| AH-R11, AH-R12; structural/story rollback | Owner-blocked on exact empty/new/drop/transfer/skill/story pairs and dependent-state mapping. Warning text cannot make invented opaque instances safe. F5/F7 exist centrally but there is no safe writer to adopt here. |
| AH-R14; progress/collectible map | External-only level index implemented. Actual quest/Chirper/Testing Ground/blueprint ownership and reward crosswalks remain owner-blocked (#31). Saved level presence is not completion. |
| AH-R15, AH-R16; accessible graph/map views | F9 adopted with bounded views, Unknown states, evidence navigation and text alternatives. Real prerequisite trees and regional collectible coordinates need the asset/capture crosswalks above. No edges or location transforms are fabricated. |
| AH-R17; semantic diff, corruption and conformance | Existing F1 and parser/write suites retained; completion tests add spatial bounds/navigation, live inventory queries, large authored lists, query scope undo and cache isolation. |
| AH-R19; scope/permissions/acceptance documentation | Embedding and experimental writes approved by owner; notices retained. All changes stay in this plugin; no artifacts, game dumps, tooling or framework edits. Original download/full-set export remain host features. |

Agent work complete for the available framework and supplied corpus. Remaining
game-format/compatibility items require the captures, assets or console checks in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

## Outer format and integrity

`sdimg_9/ue4savegame.dpx.sav` is a slot, not GVAS. Its
`sdimg_sce_bu_9` twin is identical, not an independent gameplay state.

| Region | Observed layout |
| --- | --- |
| Header | LE int32 tag 1, save version 2, package 522; engine 4.27.2, branch FString, custom version format 3 and GUID/int32 entries |
| SlotInfo | FString class, int32 byte count 1,061; payload at file offset 1,192; UE property tags, `None`, zero int32 |
| SlotData | FString class, algorithm FString `Oodle`, int32 compressed byte count 123,892; framed data at file offset 2,302 |
| Frames | 14 six-u64 headers: magic `0x9E2A83C1`, block size 131,072, compressed/plain sizes, repeated compressed/plain sizes, followed by the codec stream |
| Codec | Kraken decoder 6 (`8C 06` in this sample); shared stored-block `CC 06` streams also accepted |
| Lengths | 13 decoded chunks of 131,072 and one of 47,455; exact aggregate 1,751,391 |
| Trailer | 20 bytes: SHA1(file excluding trailer), required for compatibility, not authentication |

Other Oodle decoder IDs are rejected with an explicit Kraken-only message.
No Mermaid, Selkie or Leviathan implementation or fallback is included. Compressed
and decoded sizes are bounded before allocation. See the codec README for its
work, scratch, recursion and unsupported-stream limits.

## Decoded grammar and preservation

Offsets here are in **decoded SlotData**, including its prefix; they are not
compressed-file offsets. See [EVIDENCE.md](EVIDENCE.md) for measurements and F1.

1. An int32 byte-array length is exactly the remaining decoded byte count:
   1,751,387. This is the serialized `DataBytes` array inside compression.
2. SlotData's tagged UObject begins at offset 4: `Map`, `TimeSeconds`, then
   FString `None` at `0x8B` and a zero int32. Manual data begins at `0x98`.
3. An int32 count introduces five subsystem records. A record contains a name
   FString, **two class-related FStrings**, int32 data length and data bytes,
   int32 tag count and tag FStrings. The second class string is not a constant
   zero: it can contain a path, including when the first string is `None`.
   Their precise engine/fork meanings remain inferred; both are retained.
4. The main level begins at `0x129B`: name FString, actor count, actors. It is
   followed at `0x11D65` by a count of 261 sublevels and their same-shaped records.
   No stock GameInstance/general-filter/level-script layout is assumed.
5. Actors extend the object record with two byte-sized bit flags, 40 native
   transform bytes, int32 movement flag, optional 24 velocity bytes, component
   count and components. Components extend the object record with 40 transform
   bytes. `None` record names consume only their name. Native numeric semantics
   are not exposed as editable fields.
6. Each payload retains its full span. Tagged sequences are inspected only at
   the known payload start, never by scanning for plausible names. Recognized
   inventory structs, selected struct arrays and the grid's int64→struct map
   are bounded and consumed exactly. Native/unrecognized property payloads
   and any post-tag tail remain opaque, with hex access.
7. The player inventory component has a manual count/FString prefix (six
   identifiers here), followed by tags. That list is not treated as stack
   quantities. Slot `Amount`, `ItemId` and `ItemDataAsset` fields are read from
   explicit tags. Grid/map identities and empty-slot identities remain distinct.

The parser consumes the complete outer record grammar to the final byte.
Nested payload inspection is deliberately partial: an unrecognized native body
is retained, not asserted valid as a tagged UObject. No independent inner
checksum has been established. A valid outer SHA-1 is not gameplay validation.
Record IDs use parent/kind/both-class-strings/name; duplicate names receive occurrence suffixes.
Those suffixes are ambiguous across reordered duplicates until controlled
captures prove a stronger identity. Scalar static-array indices are retained.

Limits: 128 MiB file/decoded aggregate; 4,096 custom versions and levels; 1,024
frames, metadata properties or properties per tagged sequence; 32,768 records
or collection entries; 100,000 total property tags; depth 16; 4,096 code units
for names; 1,048,576 for scalar strings; 16 million code units each for record
and property identity paths. Lazy expansions contain at most 100 top-level
nodes; tables and blobs are paged. Impossible framing/counts, unsupported codec
variants and malformed inspected structures throw `SaveFormatException`.
Known fixed-width primitive tags require their exact payload sizes in SlotInfo
and inspected SlotData sequences; boolean tag values must be 0 or 1. Unknown
types/native structures remain opaque. The robustness review rechecked the
[SaveExtension record source](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/Records.cpp)
and [Epic's property-tag reference](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/CoreUObject/FPropertyTag)
on 2026-10-29. These describe structural leads; the fixed-width rules are also
verified against the supplied PS5 bytes and authored corruption fixtures.
Inventory `Amount` and `ItemId` must fit the supported signed 64-bit range;
oversized unsigned values reject with a field-specific `SaveFormatException`.
Representable unsigned values remain inspectable; unsigned Amount tags do not
become write targets.

## Validation

Run from the repository root:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart.Tests

dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

The sample-backed suite covers the complete lazy view, conformance, pack validity,
known layout/spans, every synthetic inner/outer truncation, valid-hash malformed
inner data, unsupported Oodle variants, 500 seeded inner mutations, exact no-op
preservation, F1 alignment/corpus evidence, unknown/native retention, F8 whole-index
queries, and F4 replacement/unknown-reference behavior. Sample-dependent tests skip
when private artifacts are absent; that is not counted as sample validation.

A separate scratch Python parser independently consumed the decoded manual
record grammar and confirmed all record/level counts and the final boundary.
It used the shared decoder's output, so this is **not independent codec agreement**.
The write suite independently reads every new stored `CC 06` frame as literal
bytes without the production parser or shared decoder. Reused compressed streams
must match the original exactly and use the original decoded baseline. This proves
new stored output agrees byte for byte with the intended edit; it is **not** a
second compressed-Kraken implementation. Full independent compressed-corpus
agreement and PS5 console reload remain open. Amount writes get an F1 byte diff
limited to the intended payload; reference writes test growing/shrinking strings
and retention of unrelated records, leaf payloads and opaque tails. Scalar and
selected-bulk session tests cover undo/redo, stale plans and atomic invalid batches.
The completion suite passed **59 tests, 0 failed, 0 skipped**, with
`SAVETOOLKIT_ARTIFACTS` exported in the test command. API build passed with
**0 warnings and 0 errors**. No timing assertions are used. A diagnostic run on
the largest (and only unique) sample
measured a first changed export at about 136 ms / 29.1 MB allocated and ten repeated
validated exports at about 0.6 ms / 0.50 MB allocated per export; this includes
returned-buffer copies and test overhead and is not a cross-machine benchmark.

### Malformed-input review (2026-10-29, #40)

The two medium findings are fixed: malformed fixed-width payload sizes/metadata
boolean values now reject, and overflowing inventory integers report the field
name through `SaveFormatException`. Regression tests first reproduced 16 silently
accepted primitive corruptions and four `OverflowException` cases.

The review adds 43 cases (42 sample-free): exact-width/unknown-data controls,
unsigned inventory boundaries, actor/component and checksum-valid outer
truncations, collection counts, depth 16/17, nested element truncations with all
enclosing lengths repaired, and in-memory mutations of real nested scalar tags.
It retains the earlier FString and aggregate-prefix truncation tests. Native
payloads and unknown tails remain opaque; accepting them is not a claim that
their uninspected grammar is valid. No additional parser defect is claimed for
the characterization tests, and no findings are deferred.

Release validation: **157 passed / 0 failed / 0 skipped** with private samples
(baseline 114). With samples unavailable: **125 passed / 0 failed / 32 skipped**;
all new synthetic cases execute. The API Release build with `--warnaserror`
reports **0 warnings / 0 errors**. Existing byte-identical no-op, write/reopen,
history and conformance tests pass. Experimental maturity and warnings remain.
All review changes are confined to this plugin; no sample bytes were committed.

### Performance review (2026-10-29, #40)

Record inspection retains short member segments and constructs full leaf IDs only
when expanded. The same complete identity-length budget is still checked at Open.
The immutable record explorer retains one last ordered query result and a copied
filter snapshot; page nodes remain fresh. Combined class labels are reused.
Inventory queries retain one current asset description per slot, refresh it after
asset edits/undo, and match live numeric values using stack-formatted spans.
Selected action plans resolve native row ordinals without scanning unselected slots.
The inventory query still filters/sorts the complete live model on each request.

Harness and baseline source snapshot: `D:/Temp/saveeditor/review-results/perf-atomicheart/`.
Run `./run.ps1 -Baseline -Label before` and `./run.ps1 -Label after` there.
Release/.NET 10.0.11, SDK 10.0.400, tiered compilation disabled, five warmups
and 21 synchronous measurements per operation. Medians use Stopwatch and
[`GC.GetAllocatedBytesForCurrentThread`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0);
managed allocations exclude native memory and are not retained-heap sizes.
Import and pack setup are excluded. Largest/sole unique snapshot: 126,214 bytes,
1,751,391 decoded bytes, 14 chunks, 4,063 objects. Pages request 20 rows.

| Operation | Before → after median ms | Before → after allocated bytes |
| --- | --- | --- |
| Open pinned + first root | 43.4325 → 46.9232 | 34,988,656 → 29,192,544 |
| First root on prepared fresh document | 0.0192 → 0.0168 | 11,104 → 9,560 |
| Inspector construction | 9.0620 → 3.3976 | 9,136,480 → 3,340,504 |
| First class-sorted record page | 0.4824 → 2.3756 | 684,408 → 496,728 |
| Subsequent record page / search / class sort | 0.1428 / 0.4238 / 0.4126 → 0.0056 / 0.0035 / 0.0056 | 213,960 / 13,072 / 684,264 → 17,648 / 11,088 / 17,872 |
| First inventory search | 0.0591 → 0.0538 | 136,208 → 148,112 |
| Subsequent inventory page / search / amount sort | 0.0718 / 0.0864 / 0.0712 → 0.0431 / 0.0479 / 0.0456 | 157,288 / 137,072 / 157,288 → 158,056 / 132,680 / 158,056 |
| One selected retail quantity preview | 0.0179 → 0.0004 | 9,144 → 1,360 |
| Authored 10,000-slot search / one-row preview | 4.3903 / 0.9822 → 1.5468 / 0.0005 | 5,021,184 / 674,368 → 8,528 / 912 |

Tradeoffs: the first record query sorts and retains its complete result instead
of partially sorting one page; subsequent matching pages reuse it. First inventory
search allocates descriptions which remain bounded to the slot count. Rich inventory
page rendering and whole-document previews have no meaningful allocation improvement.
Open allocation falls about 16.6%; total Open latency varied with machine load,
including unchanged codec/parser phases, so no total-Open speedup is claimed.
These are warm-process diagnostics, not cold-start or console benchmarks.

All 15 added sample-free cases pass; seven performance checks failed against the
corresponding pre-fix code. Regression gates assert allocation bounds or catalogue
lookup counts, never elapsed time. Full Release suite: **194 passed, zero failed
or skipped**; API Release build: **zero warnings/errors**. Shared cursor/FString,
robustness, byte-identical no-op, write/reopen, stale-plan and undo tests remain green.

### Reusable codec workspace adoption (2026-10-29, #40)

`SlotDataDecoder` retains its aggregate bound and exact plain buffer, and reuses
one disposable `OodleWorkspace` across all frames. Each frame decodes into its
exact destination slice; contextual errors and final aggregate validation remain.
The codec guarantees exact output or throws, so no separate result-length check
or per-frame result allocation/copy is needed.

The allocation regression exercises the production decoder on the 14-stream,
1,751,391-byte sample. It subtracts an identical plain-array copy and record-parser
pass to exclude aggregate/index allocations; reads, framing and assertions are
outside measurement. With five warmups and the median of three synchronous
measurements (Release, .NET 10, tiered compilation disabled), managed codec
allocations fell from **2,384,928** to **632,552 bytes** against a **917,504-byte**
cap. The earlier codec review's 10.5 MB baseline predates the shared codec changes.
This is allocation evidence, not a latency or retained-memory claim. Exact error
messages, chunk offsets and inner exception types are pinned by sample-free tests.

## Sources and attribution

Retrieved 2026-10-28. Sources were read, not executed as downloaded programs.
The repository and derived public-tool label dataset are GPL-3.0-or-later.

| Dataset / reference | Source and author | Licence / changes and use |
| --- | --- | --- |
| Embedded weapon/ammunition names | [AtomicHeartMenu features.cpp, fe36f468](https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/src/features/features.cpp), Copyright (C) 2026 Skorchekd | [GPL-3.0-or-later](https://www.gnu.org/licenses/gpl-3.0.html), including [upstream attribution terms](https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/NOTICE). Original 37 weapon and 8 ammunition label/object-name pairs retained; normalized repeated object names to exact short keys; categories and cautions retained. Twelve labels now use official English names; original runtime aliases remain attributes. No runtime code/offsets or limits copied from the tool. Credits retained: Skorchekd; Dumper-7 by Encryqed; MinHook by Tsuda Kageyu; Dear ImGui by Omar Cornut (ocornut). Dataset stays GPL-3.0-or-later. |
| Publisher weapon art and English excerpts | [Mundfish weapons](https://ah.mundfish.com/weapons/), individual page URLs in records and `assets.source.md`; Slimao Ltd. / Mundfish; retrieved 2026-10-29 | `Rights reserved by Slimao Ltd.; used with attribution per owner decision 2026-10-29`. Twelve 64x64 WebP renders and short English excerpts; no full official descriptions. Original image fingerprints, crops and output hashes in the sidecar. |
| NORA resource, consumable and module UI | [Crafting guide](https://whatifgaming.com/atomic-heart-crafting-guide/), Bilawal Bashir / WhatIfGaming, 2023-02-22; game content Slimao Ltd. / Mundfish; retrieved 2026-10-29 | `Rights reserved by Slimao Ltd. and WhatIfGaming; used with attribution per owner decision 2026-10-29`. Eight resource glyphs, two module glyphs, one Small Neuromed render; names/effects/recipe facts transcribed and descriptions paraphrased. No screenshots or logos embedded. |
| Shok name/cost reference | [All Abilities](https://gameriv.com/all-abilities-atomic-heart/), Md Obyead / GameRiv, 2023-03-09; retrieved 2026-10-29 | `Rights reserved by GameRiv; used with attribution per owner decision 2026-10-29`. Selected nine unambiguous Shok name/cost rows; authored short effect summaries. Guide English spellings are Inferred, not build-matched localization; no saved IDs, ownership or prerequisites asserted. |
| Identifier formatting and container/path categories | Toolkit-authored algorithms in `InventoryLabels.cs`; current save tags only | Original GPL-3.0-or-later presentation logic, not localized game text or an extracted private dataset. Raw IDs and Unknown confidence are retained. |
| External location/skill guides | [Atomic Heart Wiki locations](https://atomicheart.fandom.com/wiki/Locations), [skills](https://atomicheart.fandom.com/wiki/Skills), wiki contributors | Links only. Fetches were restricted (402); no page text, costs, prerequisites, images or coordinates imported. These links are not evidence of PS5 field semantics or embedding rights. |
| SaveExtension format research | [FileAdapter.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/FileAdapter.cpp), [Records.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/Records.cpp), [SlotData.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/SlotData.cpp), [LevelRecords.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/LevelRecords.cpp); Copyright 2015–2020 Piperift | [Apache-2.0](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/LICENSE). Structural reference for original C# implementation; fork order and boundaries measured independently. No verbatim C++ port. |
| Shared Kraken implementation | [TekkenMusicModManager, 8737709](https://github.com/AaryanKh/TekkenMusicModManager/blob/8737709e840c74efc935e23ac0d33cdb6aaef8f5/src/Tmm.Core/Pak/Kraken.cs), [Powzix ooz, 0503806](https://github.com/powzix/ooz/blob/05038060aa68f9187ae9923b2388ca8db40e58d1/kraken.cpp) | GPL-3.0-or-later. Used through the shared codec, no plugin-local port. See [codec notices](../../src/SaveToolkit.Codecs/README.md) and root THIRD-PARTY-NOTICES. |
| Stored-block framing research | [hl-savetools hlsave.py, e568ded](https://github.com/producedbytmsh/hl-savetools/blob/e568dedcda2611f54cc793e6d0237c749ae25094/hlsave.py) | MIT. Comparative framing lead, rechecked for the write round; not evidence that a rewritten PS5 Atomic Heart save loads. No Python writer ported; output uses the existing shared managed codec. |
| SHA-1 reference | [RFC 3174](https://www.rfc-editor.org/rfc/rfc3174) | Algorithm/vector reference; hashing uses .NET. |
| F14 inventory presentation | [Framework issue #38](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/38), toolkit maintainers; final implementation comment read 2026-10-29 | Shared metadata/query contract from `docs/PLUGIN_GUIDE.md`. Existing upstream SaveExtension research was rechecked; it does not define Atomic Heart vacancy semantics. The conservative combined sentinel above was verified against the supplied bytes, not inferred from zero quantities or unrelated game formats. No external code/data imported this round. |

Attribution lives beside the data in `ReferencePacks/atomic-heart.items.en/items.source.md`,
in `assets.source.md` / `guide.source.md`, the pack manifest, this table, `PluginInfo.References`, per-field descriptions
and the host's Sources panel. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
The Atomic Heart Fandom pages checked declare CC BY-NC-SA; their text/images are
not embedded. No permission decision is pending for the shipped catalog.

## Owner capture and validation checklist

- [ ] Provide console game build, region, DLC and slot labels for every capture.
  Retain a clean recovery copy. Use `PPSA10695_<event>_before.zip` and `_after.zip`.
- [ ] Capture an idle load/save pair without gameplay changes to identify churn.
- [ ] Capture one known Neuropolymer gain, then one spend; note displayed counts.
- [ ] Capture one material pickup and one craft/spend, recording exact materials.
- [ ] Capture one ammunition pickup and one fired round/reload, recording loaded
  and reserve counts separately. Include one small healing-item use.
- [ ] Move one identified stack backpack→NORA storage and back; record instance,
  amount and cell positions. Include an empty→populated slot transition.
- [ ] Buy/refund one skill; equip/upgrade one weapon; learn one blueprint, each
  in a separate pair. Record resource costs, level and effects.
  Include the skill/upgrade menu and a matching build/locale asset/localization
  pack so exact skill/module IDs, prerequisites, friendly resource/consumable
  labels and costs can be mapped. Current skill collections are empty; weapon
  references alone do not identify upgrade levels.
- [ ] Collect one Chirper, one Testing Ground reward and one objective advance,
  separately. Provide early/open-world/late/NG+/DLC states before broad claims.
- [ ] Compare original compressed chunks with a separately reviewed independent
  Kraken decoder. Resolve unknown inner integrity and duplicated state separately;
  the stored-output literal oracle does not establish these.
- [ ] On a disposable copy, apply one small existing Amount edit. Record output
  growth and slot capacity, restore, load, inspect inventory, travel, save, restart
  and reload. Record rejection/normalization as well as success. The changed frame
  uses stored blocks; PS5 acceptance is unproven.
- [ ] Test one same-category weapon replacement and one ammunition replacement
  separately. Record raw old/new asset IDs and ItemId; inspect equip/use, modules,
  reserve/loaded ammo, ownership and capacity; save/restart/reload and re-export.
- [ ] Test a selected multi-slot Amount batch and a full eligible-slot batch
  separately, with F6 preview/skipped rows. Observe/use/spend, save/restart/reload;
  compare intended and unrelated decoded spans against an idle control. Include
  Amount zero; check normalization without assuming structural removal.
  Also test an F8 filtered whole-query batch with an excluded row; verify the
  excluded row and unloaded matching rows after resave/reload. For the location
  index, identify the active region separately from saved-but-unvisited levels.
- [ ] Record title/build/region/DLC/slot, visible before/after values, growth,
  normalization, rejection and persistence on
  [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31) and
  [#12](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/12).
  Only the owner may promote maturity to Stable.

## SDK requests

None blocking the enabled writes or the new F9 views: F1/F2/F4/F6/F8/F9/F12/F13 and F11 suffice. F11's managed Kraken decoder
and stored writer are used directly. Non-Kraken variants remain unsupported.
Independent compressed-codec agreement and console captures are validation
requests. Coupled skill/loadout, structural and story operations need a safe
serialization/dependency model; no framework changes are requested here.
F2 attribution licence fields are limited to 100 characters. The mixed item dataset uses `GPL-3.0+` shorthand alongside the complete Slimao owner-decision notice; original GPL-3.0-or-later terms and full screenshot rights remain in author/modification metadata and sidecars. The source-specific guide catalogues keep each rights notice within that limit. No framework change was needed.
No SDK requests remain for F12/F13/F14. Original decoded record/hex inspection
remains reachable; the inventory uses only F8, SaveDiff explicitly includes empty
rows, and the production decoder hook covers native scalar evidence. No framework
files were changed.

Performance follow-up (#40), deferred outside this plugin: F11 Kraken decoding
allocates padded input/output, exact-output copies and entropy scratch separately
per independent stream. All 14 sample chunks allocate about 10.46 MB in the codec
alone; the plugin additionally aggregates their decoded output. A caller-owned
destination/workspace API could reduce copies/scratch churn while retaining exact
consumption, padding, work budgets and corruption rejection. No codec/SDK files
were edited. SQLite is not part of this format. Open decodes each chunk once;
queries/plans do not decompress or parse again, and the existing serialization
cache retains full first-changed-output validation and isolated output copies.


## Asset round results (2026-10-29)

- **23 unique icons, exactly 12,624 new binary asset bytes**, all 64x64 WebP; well below the 4 MB plugin budget. No full screenshots or source renders are embedded. Text JSON is minified. Twelve official weapon excerpts plus 20 authored/screenshot/guide summaries were added or replaced (32 enriched descriptions).
- Items and inventory F3/F8 cells show icons; the F4 picker receives each record's pack-qualified `assetId`. Reference guide tables expand lazily and display descriptions, costs, raw/authored IDs and source/rights text. Host F2 loading validates and caches the pack once; repeated asset resolution returns the cached object.
- Exact sample-ID joins: Metal Parts, Synthetic Material, Small Neuromed Capsule. Those enrichment records cannot be used for item replacement. All prior Amount bounds and warnings remain. Unmapped `DA_Item_Jelly_Resource` is not guessed to be Neuropolymer.
- KS-23 **Additional Gas Vent**, Basic Grip level 1: **19 Metal Parts**. KS-23 **Cartridge Module**, basic installation: **25 Metal Parts, 12 Synthetic Material, 3 Chemistry**. Small Neuromed screenshot effect: **50 health**, recipe **2 Synthetic Material, 4 Biomaterials**. These are screenshot reference facts, not verified PS5 rules or new caps.
- Nine Shok guide costs are reference-only. Their authored `reference/shok/...` keys cannot replace native skill IDs. The saved three empty skill collections retain Unknown state and no prerequisite edges. Other branches contain source ambiguities and were not imported. Full exact localized skill text and build-matched limits remain unavailable.
- **No map backgrounds**: the evidence view lists 262 saved levels without coordinates or a supported transform. It keeps the text/external-guide fallback; art cannot establish marker positions.

First-load diagnostic, Release .NET 10, largest/sole unique 126,214-byte sample, three alternating fresh-process runs before/after; no timing assertions or speedup claim. The before project was reconstructed from the original tracked plugin in a temporary folder, using the same SDK/Core/codec dependencies. The stopwatch covers `ReferencePackStore` construction/pinning, plugin Open, BuildView and the first complete 90-slot inventory query; current-thread allocations use `GC.GetAllocatedBytesForCurrentThread`. Sample loading happens before the stopwatch. Medians:

- Cold pack + Open + view + inventory: **274.94 ms / 37,834,744 allocated bytes before**, **250.69 ms / 40,119,880 bytes after**.
- Reusing the pinned pack, Open + view + inventory: **113.33 ms / 36,987,624 bytes before**, **97.75 ms / 37,079,032 bytes after**.

Timings vary with JIT and scheduling. The new cold allocation cost is about 2.29 MB; repeat-open overhead is about 91 KB. The reference guide does not materialize rows until expanded. Hash/type/rights resolution, duplicate rejection, live icon replacement/undo, reference-only joins, unchanged Amount bounds, byte-identical no-op/undo, unknown spatial state and missing-pack fallbacks are covered by tests.

Validation: **66 passed, 0 failed, 0 skipped**, with `SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts` exported in the same test command. Full API build: **0 warnings, 0 errors**. Image audit verifies 23 unique 64x64 WebP files and every manifest hash. All handwritten C# files stay below 300 lines. All changed repository files are under `plugins/AtomicHeart`.

## Licence exclusions

| Source URL and revision | Licence found | Refused material / reason | Current approach | What would unblock reuse |
| --- | --- | --- | --- | --- |
| [Wiki locations](https://atomicheart.fandom.com/wiki/Locations), unpinned, checked 2026-10-29 | Previously recorded CC BY-NC-SA; version not recorded; fetch still inaccessible (402/403) | NC wiki prose/images remain excluded under rule 9; publisher assets from other sources are now imported under the owner decision. | Existing external-only level index; no background because native coordinate alignment is unknown. | Compatible wiki terms and accessible content, plus supported native level/coordinate joins for a background. |
| [Wiki skills](https://atomicheart.fandom.com/wiki/Skills), unpinned, checked 2026-10-29 | Previously recorded CC BY-NC-SA; version not recorded; fetch still inaccessible (402/403) | NC wiki text/cost/prerequisite dataset remains excluded. | Attributed GameRiv Shok subset in a separate reference guide; saved graph states remain Unknown. | Compatible wiki terms and build-matched skill/native ID mapping. |

The two central register rows already record these NC exclusions; they remain applicable. Publisher art and all-rights-reserved guide sources are resolved by the 2026-10-29 owner decision and imported above. No central register file was edited because this worker is confined to its plugin folder.
