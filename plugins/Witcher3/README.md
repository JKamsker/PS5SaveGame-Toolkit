# The Witcher 3: Wild Hunt – Complete Edition (`witcher3`)

Title: **PPSA04018**. Version: **0.6.0**. Maturity: **Experimental**.
Supports player level, unspent XP, available ability points, Crowns, inventory quantity/durability,
non-core skill rank and existing Gwent collection-count edits for the
supplied PS5 SAV3 schema **64 / 23 / 163**. The preserving writer passes edit conformance and sample
round trips; **edited saves have not been loaded on a console**. When the outer parser recognizes the
container/directory, other SAV3 versions and saves without an unambiguous player identity retain the
read-only explorer; arbitrary future formats are not guaranteed to open. Stable requires human in-game testing.

Round 2 replaces the 18-item starter pack with **4,011 item references, 107 abilities/mutations and
225 Gwent indices**, with revision-pinned CC BY-SA attribution and retained MIT notices. It adds
full-catalog browsing, named saved ability/card joins, community Gwent acquisition/copy advice, and
read-only recipes, journal paths and facts. See [CATALOG.md](CATALOG.md) for coverage and licensing;
[EVIDENCE.md](EVIDENCE.md) records sample evidence and remaining implementation limits. The Phase 1 rich-read slice
is implemented for supplied fixtures; broader compatibility metadata still needs owner captures.
Phases 2–4 remain partial. Completion adds F9 skill/mutation graphs and native map-tag lists, F8
ability/map queries, F4 ability identity references and F6 ordinary-stack refill. The requirement and
acceptance audit is in [COMPLETION.md](COMPLETION.md); remaining evidence is tracked in #31.

## Framework feature adoption

| Feature | Status | Adoption / concrete scope |
| --- | --- | --- |
| F1 SaveDiff | Used | Native F1 tests use a thin `HookSaveDecoder` adapter over the production F12 hook; intended semantic paths and independent byte audits cover every sample. Positional ability identities are explicitly flagged as incomplete alignment evidence. |
| F2 reference packs | Used | Two pinned packs: 4,011 item references, 107 abilities/mutations and 225 Gwent indices; per-record attribution and Sources panel. |
| F3 rich cells | Used | Inventory/ability/catalog detail sections, inferred-mapping badges, field confidence/warnings and provenance. No unlicensed icons embedded. |
| F4 reference pickers | Used | Saved skill/mutation identities use read-only `ReferenceSpec("abilities")` with raw fallback and F13 browse-only category/search/sort/details. Lookup action parameters remain selectable references. Rank/count writes stay numeric; identity replacement has no safe writer. |
| F5 structural history | N/A | No safe item/skill creation, deletion, reordering or identity/reference/ownership cleanup writer exists. Scalar omitted-zero property insertion is replayable by value ID; it does not create a gameplay record. Controlled ownership captures are in #31. |
| F6 planned actions | Added | Repair plus imported-quantity refill; document, explicit row and Core-resolved #27 F8 query scopes, exclusions/warnings, stale/forged revalidation and one undo step. |
| F7 document groups | N/A | Only selected `.sav` changes. Same-stem `.req` is an invariant content list, not a coupled gameplay/checksum file; no demonstrated cross-file edit invariant. Core handles identical backup mirroring. |
| F8 queryable lists | Added | Existing full inventory/catalog/recipe/fact/journal queries plus every ability array and native map tags, with full-list search/filter/sort and bounded pages. Lazy details remain for reliable F9 navigation to unloaded data. |
| F9 graph/map nodes | Added | 102 saved skill definitions, 13 mutation definitions/11 native prerequisite edges, and counted known/discovered/disabled map tags. Text alternatives, attributed raw fallbacks, stable linked fields, bounded nodes and external map links. |
| F10 exact/nullable/union | N/A | This binary schema stores fixed integer/float32/float64 values, not exact JSON number lexemes or polymorphic scalars. Omitted default zero is neither explicit null nor a union; using those specs would invent storage states. Existing integer specs retain exact widths. |
| F11 shared codecs | N/A | SNFH uses raw LZ4, already handled by K4os.Compression.LZ4. SaveToolkit.Codecs' Kraken/SQLite wrappers cannot decode this format; no local Kraken/SQLite implementation exists. |
| F12 | Added | `Witcher3Plugin` implements SDK `ISaveDecoderProvider`, exposing `witcher3-native-identities` for `savediff compare --decoder`. Production has no Testing dependency; the existing research decoder is a thin adapter. Sink refusal, depth/input/value bounds and positional identity limitations are tested. F8 views remain available via `--decoder witcher3`. |
| F13 | Added | All four message-only lookups publish `Mutates = false` and work on read-only saves with unchanged bytes/history/revision. Ability identities browse without writes. Catalogue category filtering and declared key/name/category sorting use F2/F13 queries; the duplicate metadata index is removed. Float duration is N/A: no float timer has proven units. |

## F12/F13 follow-up

The native decoder is selectable without loading the test assembly:

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --decoder witcher3-native-identities
```

The hook retains the existing entity/item/card/native-ability paths, resolves name indices, and
fingerprints opaque properties. It stops as soon as the sink declines output, enforces input and
semantic-path depth bounds, and uses the host's value-length/value-count caps. Ability records lacking
native keys retain `unnamed-slot-N` evidence paths. If those records contain properties, their
positional alignment is unproven: the hook calls `MarkTruncated`, so sample comparisons explicitly
report incomplete input even when all values fit. This is separate from output-budget truncation;
missing paths alone cannot prove removal. Independent exact-byte audits remain required for writes.

Item, ability and Gwent lookup actions return attributed messages through F13's isolated inspection
path, including on unsupported-player read-only saves. Saved skill/mutation identity fields open
browse-only reference pickers; unknown current raw IDs remain visible and cannot be replaced.
Catalogue tables use exact category filters and declared Key/Name/Category
sorts with stable raw-key ties and pages of at most 200. Custom catalogs without declared sort keys
retain their own file order. Full metadata text search, additional exact presentation-column filters
and saved-state/description sorts read bounded catalog pages on demand without retaining a second
metadata index. Text search still covers names, raw IDs, categories, descriptions, attributes and saved state.

The `63580dc` F6 reference-parameter fix is N/A here: repair/refill have no parameters, and no enum
fallback exists. A float duration hint would invent units for currently opaque fact time/expiry bits,
so those stay raw. No new format or gameplay claims are introduced; existing attributed format
sources below remain the basis of the decoder.

## Enabled writes and their evidence

Owner decision (2026-10-28): **Enable writes. Keep warnings.** Console validation and controlled
captures are tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31); they do not
gate these controls. Every new control remains **Inferred**, and the plugin stays Experimental.
The game may reject the edited save or rebuild dependent state. Passing binary tests does not prove
the gameplay effect, reward accounting, achievement behavior or persistence after a native re-save.

| Write | Confidence | Evidence | Integrity status | Owner validation needed (see #31) |
| --- | --- | --- | --- | --- |
| Existing inventory durability | Inferred | Native float32 at item +19; converter layout and all eight bounded PS5 records. Editable only when original is in 0..100; -1 and other unsupported ranges stay read-only. | Exactly four body bytes; rebuild LZ4 descriptors; reparse and verify intended durability plus all inventory metadata. No known SNFH checksum. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): damaged weapon/armor, load, verify displayed durability and combat, re-save/reload, re-export. 100 is a conservative repair target, not a proven legal PS5 limit. |
| Non-core existing skill rank | Inferred | Player-owned counted SSkill array, saved `level`/`maxLevel`; converter/native core identities. Bound 0..saved maxLevel. Omitted rank zero is inserted using existing dictionary names. | Original properties retained; changed array length and containing PORP/BLCK/SS/directory pointers repaired; reparse and verify every skill property's bytes. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): rank up/down, check free/spent/path points, slots, prerequisite UI, modifiers/stats and combat; native save/reload/re-export. No purchase/refund/respec logic runs. |
| Existing Gwent ordinary/leader copies | Inferred | Typed cardIndex/numCopies pairs before the deck boundary; MIT tracker and 27 indices / 35 copies in all eight records. Ordinary 0..3, leaders 0..1, widened only to preserve an already larger original count. | Exactly four body bytes per pair; LZ4 framing rebuilt; reparse and compare every card identity/group/count. Deck bytes remain unchanged. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): duplicate count and leader removal/restoration, collection/deck/gameplay/quest/achievement checks, save/reload/re-export. Counts can invalidate a deck; tracker targets are not legal caps. |
| Repair existing items (F6 bulk) | Inferred | Same durability encoding; preview lists each old/new value and skipped sentinel/unsupported/already-100 row. Document, explicit-row and resolved F8 query scopes. | One atomic edit/undo group; pure preview, apply-time revalidation, no direct invocation; only intended float ranges change. No money or repair rewards change. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): bulk repair two damaged items, check unrelated inventory/Crowns/achievements, native save/reload/re-export. |
| Refill ordinary food/water to imported quantity (F6 bulk) | Inferred | Exact observed native IDs `Bread` / `Bottled water`, unequipped, unslotted and untagged only. Target is each instance's imported quantity; already higher values are preserved. No claimed legal cap. | Original fixed-width quantity writer; exact byte/F1 intended ranges, reparse, no-op/undo identity and F6 plan revalidation. Unknown game dependencies remain warned. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): lower then refill an existing eligible stack, test selected/full/filter-with-exclusion scope and two eligible stacks together; inspect food slots, consumption, quantities, money/quests/rewards and native save/reload/re-export. |
| Existing level / XP / free ability points / Crowns / quantities (retained) | Inferred | Existing preserving writer, player IdTag, saved level definitions and native inventory widths. | Existing edit conformance, relocation/reparse and exact no-op/undo checks remain. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): original spare-slot validation checklist in EVIDENCE.md. Level-up/stack dependencies remain warned. |

The first four rows shipped in the write round; refill is **one additional write capability** this
completion round. Core skills and definition/sentinel
records remain intact. A missing zero property inside an existing skill is a scalar edit with a stable
identity; it does not add a skill or inventory record. Two rank pages can be edited together with
progression dictionary growth, durability and Gwent counts, then undone together.

Structural item creation/deletion/replacement, stash/Roach transfers and equipment swaps are not
implemented: native identity allocation, references and ownership cleanup remain undecoded, so
guessing would risk corrupting unknown structure. Mutation research, coherent respec/build presets,
recipe grants and story/world transactions likewise lack a preserving transaction model for their
related systems. Raw facts, journal state, generic stats and slots remain inspection data. These are
implementation/structure limits, not a wait for console approval. No F5 or F7 action was prepared in
the earlier handoffs; `.req` remains an unchanged content list, with no proven multi-file dependency.

## File roles

- `CheckPt.*.sav`, `Manual.*.sav`: gameplay; claimed with matching title, `.sav` extension and
  `SNFHFZLC` signature. Other correctly framed `.sav` slot names are accepted too.
- Same-stem `.req`: required content metadata, **not a checksum**. Every supplied primary and backup
  contains exactly `content0;content1;content2;`, consistent with the cited converter's requirements
  handling. The list is preserved; player/item edits do not change content requirements.
- `user.settings`: INI-style configuration; not claimed or modified.
- System files, JSON companions, thumbnails and backup siblings are never rewritten as a side effect.

The plugin emits only the selected `.sav`. Core may mirror byte-identical backup siblings during
workspace export; the plugin alone does not assemble a complete restore-ready slot. Processing uses
SDK-supplied bytes without filesystem/network I/O.

## SNFH container and compression

Words are little-endian. `SNFH` is at 0, `FZLC` at 4, int32 chunk count at 8, int32 header/data
start at 12. Descriptor triples begin at `0x10`: stored length, expanded length, optional absolute
end offset (zero allowed). Blocks follow sequentially at the declared data start. Nonzero end offsets
must equal block ends. Raw LZ4 blocks have no frame/dictionary/checksum wrapper. When stored length
is equal to expanded length, the block is stored verbatim, as in the cited native-format writer.
Every compressed decode must produce exactly the declared expanded length.

All supplied files have one compressed block, a **3,084-byte (`0xC0C`) header**, zero reserved header
bytes, zero end-offset sentinel, and no container trailer. Expanded data starts with `SAV3`.
The supplied decrypted files need no further encryption. Codec: managed K4os.Compression.LZ4 1.3.8.

The writer retains the header size and earlier expanded chunk boundaries. The final chunk absorbs
body growth up to 1 MiB (or its original size if already larger); further growth adds chunks only when
there is sufficient **zero-filled, unused descriptor capacity**. It refuses to overwrite nonzero
reserved data or move the header. It reuses unchanged compressed blocks, recompresses changed blocks,
uses verbatim storage for incompressible data, and updates count, header size, stored/expanded lengths
and absolute ends. An original zero sentinel is preserved where applicable; newly intermediate chunks
have absolute ends. Reserved header bytes and any opaque container trailer survive.

Limits: 128 MiB input, 64 MiB expanded data, 4096 chunks, 200,000 indexed/explorer entries,
128 nesting levels and a uint16-sized name dictionary. Invalid bounds/lengths raise `SaveFormatException`.

## SAV3 layout and preserving serialization

| Region | Layout |
| --- | --- |
| Header | `SAV3`, three int32 version words; samples 64 / 23 / 163, exact meanings unconfirmed |
| Final 6 expanded bytes | int32 virtual variable-table offset, `SE` |
| Variable table | int32 count, then virtual int32 offset + int32 span length per entry |
| 10 bytes before table | virtual NM offset, virtual RB offset, `SC` |
| NM | `NM`, `MANU`, int32 name count, opaque int32, byte-length ASCII names, opaque int32, `ENOD` |
| RB | `RB`, int32 count, opaque uint16 + virtual int32 offset per entry |

**Virtual offsets include the SNFH header** although they address expanded data: subtract the declared
header size to index the SAV3 byte array. Names are one-based. Indexed spans form a containment tree;
crossings/duplicate offsets are rejected. The name table is itself indexed. RB's uint16 remains opaque
(upstream calls it a size, but sample values resemble root name indices).

`BS` has a uint16 name index; the variable table supplies its span. `VL` / `OP` have uint16 name/type
indices followed by typed bytes. `SS` has int32 archive length and a separate, non-indexed stream:

- `SXAP`: magic and three version words (16 bytes).
- `BLCK`: magic, uint16 name index, int32 child-data length, nested tokens.
- `PORP` / `AVAL`: magic, uint16 name/type indices, int32 value length, value bytes.

The writer splices changed values into the **original image**. Fixed-size inventory edits change only
their quantity or durability bytes. Progression edits preserve the manager handle header, property order, unknown
properties, level definitions and unedited point fields. An absent `free` field is inserted before its
structure terminator; if needed, `free` is appended to MANU without changing existing name indices.
It then updates affected PORP/BLCK/SS lengths, containing variable spans, all shifted variable and RB
offsets, the NM/RB pointers before SC, and the SE variable-table pointer. **Stored variable-table order**
is preserved, even when it differs from physical order. Opaque RB words, MANU metadata, unknown blocks,
padding and trailers remain byte-identical; relocated blocks keep their exact contents.

No-op serialization and undo back to the original values return a copy of the **entire original file**,
without recompression or canonicalization. After any edit, `Serialize()` decompresses its own output,
requires equality with the edited body, re-parses the directories/tree/player, and compares progression
and every inventory ID/name/quantity/durability against the current model. It also verifies flags,
slots, opaque prefixes, prefix names, attributes and tags are unchanged, as well as intended skill
ranks, unedited skill properties and Gwent identities/counts. Failure throws before returning output.

## Player identity and progression evidence

Player ownership is established by matching `universe/Player/id : IdTag` to a direct
`entityData/idTag : IdTag`, then using that entity's direct SS archive and Entity BLCK. All samples
match the same 17-byte player identity. The parser requires a unique match and matching SAV3/SXAP
version words, then the direct
`levelManager : handle:W3LevelManager` property and its stored `W3LevelManager` class name. It does not
find gameplay values by searching arbitrary bytes for names or magic.

The handle has the observed `00 01`, int32 object ID, uint16 class name, then a tagged structure.
Each structure has a zero prefix, repeated uint16 name / uint16 type / int32 size / value records,
and a uint16 zero terminator. Property size includes its four-byte size word. The `points` array
contains exactly two individually bounded `SSpendablePoints` structures. Omitted fields default to
zero; their absence is preserved unless that value is edited.

| UI field | Stored location | Evidence and constraints |
| --- | --- | --- |
| Character level | `levelManager.level : Int32` | Game script declares the saved field; bounded to 1..highest saved level-definition number (50 in these samples) |
| Unspent XP | `levelManager.points[1].free : Int32` | Published converter's point ordering + game script's free/used semantics; total XP = free + used |
| Available ability points | `levelManager.points[0].free : Int32` | Published converter identifies slot 0 as skill points; free/used semantics corroborated by the script |
| Used XP / spent ability points | Corresponding `used` fields | Displayed read-only; zero when omitted |
| Money | Player inventory record named `Crowns` | Game inventory script implements `GetMoney()` using `GetItemQuantityByName('Crowns')` |

All player fields are **Inferred**, including absent zero defaults and point-array ordering on PS5.
Stored identities, encodings, exact sample values and byte-preservation invariants are verified by
sample tests, but their effects after console loading are unverified. Level and XP edits change stored
values; they do **not** execute the game's level-up logic, grant rewards, unlock slots, or recalculate
scaled stats. Field warnings explain these dependencies. Skill/XP values must remain nonnegative and
free + used must fit int32. Existing level definitions and spent points are preserved.

## Native inventory evidence

After parsing the player's complete supported SXAP token sequence, its bounded opaque tail begins with
`00 00 01 A2 15 35 B6`, followed by the `CInventoryComponent` name index, uint16 next ID and uint16 item
count. The prefix hash matches the cited converter; its meaning is not independently established.
The plugin accepts this observed PS5 prefix at that structural boundary only. It does not scan for
component hashes, CInventoryComponent indices or Crowns throughout the payload.

Each item has a 24-byte prefix: uint16 item-name index at +0, flags at +2, opaque bytes +6..12, two
optional name indices at +13/+15, **uint16 quantity at +17**, float durability at +19, attribute count
at +23. Attributes occupy seven bytes each (name, raw value, type). Then come a three-byte unique ID,
one-byte tag count, uint16 tag names, and a uint16 equipment slot when flags include `0x200000`.
The observed tail ends with `01 00 00 00 00 00`. Names, bounds, IDs, duplicates, counts, quantities,
finite durability and terminal bytes are checked. Unknown bytes, flags, attributes and tags are preserved.

Inventory quantity controls use **1..65,535**, the observed storage width; deleting/adding stacks and
wider/alternate encodings are unsupported. Item stack limits and quest/equipment dependencies remain
unknown and have warnings. Equipment flag meaning and quantities are Inferred. Native identifiers
are always retained alongside reviewed reference labels. `Orens` and `Florens` would be separate rows
if present; none of the samples contains either. The Crowns control shares the inventory stack's
stable ID, so the two views cannot diverge.

## Views

Tabs provide overview, player progression, inventory, abilities/Gwent, reference catalogs, saved
recipes/journal/facts, variable tree, MANU dictionary and container details. F8 inventory queries search the complete model by label, native ID, category,
slot and instance ID, with exact name/ID/category/slot/equipped filters, numeric quantity/durability
sorting and deterministic instance-ID tie breaks. Queries contain at most 200 rows; linked ability/Gwent
details page 64 entries and raw details 128 entries. Chunk tables are bounded to 4096 rows. Existing quantity IDs remain
`inventory/<instance>/quantity`, including the Crowns summary; raw explorer IDs use input offsets.

F3 detail cells show raw durability (including -1), saved flags/slot, unknown prefix data, raw
attribute values/type bytes and tags. Categories and labels do not establish safe write limits or
ordinary equipment: internal appearance and scabbard records remain visible. F4's **Look up catalog
item** picker browses the pinned `items` catalog and returns attributed descriptions without modifying
the save. Replacement, equipping and add/remove are absent. Reviewed repair and conservative ordinary
food/water refill are available through F6.

F2 embeds `witcher3.catalog.en` (CC BY-SA 3.0 items/abilities) and `witcher3.gwent.en` (MIT cards).
The item catalog contains 3,670 explicit native keys and 341 additional wiki references with unpublished
native IDs, explicitly marked with non-native `wiki-entry` keys. Includes base/HoS/B&W and documented
NG+ variants/cut content; not a promise of every internal or current-patch PS5 ID. The pre-overhaul
ability catalog avoids the 2026 skill redesign. Every record includes a name, category, description,
confidence and pinned source. See [CATALOG.md](CATALOG.md) for extraction, crosswalks and gaps.

F8 catalog and saved-data queries search the complete selected dataset, filter and sort with stable
row keys and at most 200 returned rows. Facts and journal path details page 64 entries. F4 catalog
pickers inspect references without edits. Inventory, skill/card and recipe joins show attribution;
Sources exposes dataset licenses and modifications. Wiki-only keys never join inventory native IDs.
The original `Open(file)` uses an empty context and retains raw IDs; the host supplies pinned packs
through `Open(file, references)`. Unknown/modded IDs stay unmapped. Compatible packs are build-agnostic
or explicitly tagged `sav3-64.23.163`; the tuple is not a known PS5 patch. No icons or CD PROJEKT
resources are embedded. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The bounded ability reader reaches the player's `W3PlayerAbilityManager` through its owning Entity
and consumes each observed tagged array exactly: 102 skills, 16 skill slots, 4 mutagen slots,
13 mutations, 6 base stats and 14 resistance entries. Sixteen level-1 core definitions are not sixteen
purchased skills. Entries without native keys retain explicitly unnamed slot labels, with no invented
skill identity. Ninety-five named skills and 12 mutations join the reference catalog; core bookkeeping
`S_Perk_08` and sentinels remain unmapped. Saved localization/icon keys remain identifiers. Unknown nested values retain hashes
and lengths. Existing non-core ranks are editable; stat/mutation/slot definitions remain read-only.

The Gwent reader validates declared ordinary/leader collection counts and each bounded pair, stops at
the manager's `SBSelectedDeckIndex` boundary and excludes deck nodes. All samples contain 27 indices /
35 copies, including 4 leaders. All observed indices join the catalog; valid empty collections work.
The tracker distinguishes saved copies from community copy expectations and acquisition/missable
advice. Aliases remain separate native indices. Not recorded means availability unknown; deck contents,
actual quest cutoffs and achievement eligibility remain unresolved. Existing counts are editable;
absent-card creation and deck edits are not implemented.

Recipes read two explicit counted `array:2,0,CName` player properties, validating exact length, unique
names and indices: 54 crafting schematics and 14 alchemy recipes in every fixture. Missing arrays do
not imply an empty known-recipe list. Unknown IDs remain raw rather than guessed from display names.
Journal reads exactly counted `SJournalEntryStatus` records and GUID/resource/flags path triples,
including empty-resource GUID-only descendants. It displays native status/unread fields without
inventing localized quest names. Counts are 170, 181, 192 and 198 across the four snapshots.

Facts validate the structurally reached SS stream, nested tuple, `SXAP`/`SBDF`, declared count, compact
narrow/wide name lengths, opaque uint16 words, counted ten-byte entries and exact `EBDF` consumption.
Counts are 429, 611, 614 and 617. Entries retain int16 value and exact uint32 time/expiry bits; no sum
or timestamp becomes quest completion. Some bounded wide-name payloads contain control bytes/invalid
surrogates: their bytes are retained and displayed through explicit hash identities. Their meaning
is Unknown. Later saves have 183 nonzero opaque words. No fact or journal write is enabled.
Optional reader failures show independent warnings and preserve the existing explorer/writer.

F9 skill graphs show saved branches, costs and current ranks. Rank zero is Unknown, not Locked;
core definitions remain Unknown even with stored rank one. Positive editable ranks are displayed as
Unlocked stored ranks, without claiming equipped/usable status. No skill prerequisite is guessed
from tier/position. Mutation arrows follow exact counted `requiredMutations` arrays; all research
states remain Unknown. Malformed/cyclic/unsupported graphs warn and leave encoded-property views.
Rank edits and undo/redo mark the structure changed so diagram state refreshes.

The Map tags tab reads the unique native common map manager's exact counted membership blocks.
Discovered tags are Found in the limited sense of stored discovery; known-only/disabled-only tags
are Unknown. No absent tag is Missing. No completion, Place-of-Power point, fast-travel entitlement,
friendly location, region or coordinates are invented. Graph/map targets open direct lazy detail
pages, including unloaded late rows; no F8 search-by-ID workaround is needed. Nodes split at 512
items and queries at 200 rows. Maps link to attributed community regional maps; NC art/coordinates
and third-party marker data are not imported. [Spatial provenance](SaveToolkit.Plugins.Witcher3/SpatialViews.source.md).

The raw explorer and encoded-property details remain read-only and show the **input image**, while
curated Player/Inventory/rank/Gwent fields and Gwent catalog counts show pending edits.
It decodes scalar bool/integer/finite float/double, CName, GUID and validated short ASCII strings.
Other handles/arrays, ROTS, unsupported strings and unknown remainder blocks remain opaque. No
SAVY signature is assumed. Unsupported inventory tails are retained without inventory controls.

## Checksums and remaining integrity limits

No header, per-chunk or footer checksum is present in the researched SNFH writer implementations.
Their writers update framing/sizes/ends and emit LZ4 blocks, with no checksum step. In the supplied
files the header has no unexplained nonzero reserved word and no footer outside the compressed body;
SAV3 ends with its table pointer and SE marker. Raw LZ4 provides no checksum. `.req` is a content list.
No checksum algorithm is guessed or applied; named `magic_number` data remains opaque.

This establishes the observed framing, not a universal proof that every game version lacks additional
internal integrity checks. Length checks cannot detect every payload mutation. Unknown future schemas,
opaque pointer semantics and console-load behavior remain open questions. Experimental maturity reflects
these limits and the passing preserving-write/conformance tests. Every new field and repair preview
warns that the game may reject the edited save; no unknown integrity word is overwritten or invented.

## Sample evidence and tests

Four distinct primary saves plus their four byte-identical backup twins are covered:

| Save | Expanded bytes | Variables | Names | Level | XP | Free ability points | Crowns | Items | Bread |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `CheckPt.53db8.7e73c800.313c5b3.sav` | 653,545 | 16,082 | 1,980 | 1 | 0 | 0 | 250 | 24 | 5 |
| `CheckPt.8559a.7e73c800.317a3e7.sav` | 702,856 | 17,399 | 2,053 | 1 | 10 | 0 | 250 | 25 | 4 |
| `CheckPt.8559a.7e73c800.31c45eb.sav` | 703,834 | 17,375 | 2,079 | 1 | 260 | 0 | 250 | 30 | 4 |
| `Manual.8559a.7e73c800.31c94f5.sav` | 724,174 | 18,445 | 2,106 | 1 | 260 | 0 | 250 | 30 | 4 |

All have early path `game\witcher3.redgame`. Used XP and spent ability points are omitted zeros.
`free` is absent from the first file's dictionary, exercising dictionary growth as well as insertion
of two omitted fields. The other files already contain `free`; available ability points are still
omitted. File order does not prove chronology or broad version compatibility.

Tests cover:

- Full conformance with a meaningful Crowns edit; no-op byte identity; every lazy value and bounded view.
- Every primary/backup's known progression, item count, Bread, Crowns and `.req` bytes.
- Combined level/XP/ability/Crowns/Bread edits on all eight files; unmodified indexed variables remain
  bit-identical after relocation; undo returns exact input bytes.
- Crowns-only edits change exactly the corresponding two expanded body bytes; deterministic serialization.
- Independent synthetic player saves with absent/present defaults, nonzero spent points, an opaque
  property, reversed directory order, two chunks, nonzero header padding and a trailer.
- New 1 MiB chunk creation and header-capacity refusal; descriptor/end recomputation; unchanged
  compressed-block reuse; incompressible verbatim blocks; edited decompression equals the intended body.
- Unsupported version/identity read-only fallback; atomic rejected batches; corrupt nested property
  lengths, point counts, inventory count/name/quantity/terminal; existing framing/directory corruption tests.

Missing samples skip via `SampleSaves`; all synthetic tests remain runnable. No sample data is committed.

F12/F13 follow-up validation: **108 passed, 0 failed, 0 skipped**, with the external artifacts
variable exported in the test/build commands. API build: **0 warnings / 0 errors**. Added production
hook registration/bounds/adapter tests, real-sample browse-only ability references, read-only Core
inspection/state/byte checks, and bounded framework category/sort delegation including custom-catalog
fallbacks. Existing F1 assertions now require the explicit positional-identity warning rather than
claiming complete record alignment. CLI `list-decoders` and native `compare` were checked directly.

Completion-round validation: **99 passed, 0 failed, 0 skipped**, with artifacts exported in the
same command and all eight save records exercised. API build: **0 warnings / 0 errors**. Includes
F9 validation/navigation/refresh, mutation prerequisite corruption, 4,096-skill and 1,650-tag bounds,
F8 queries, refill exact-byte/F1/reopen/history/scoping/rejection tests, direct type/range rejection
and tagged parent-boundary regressions. No console acceptance is claimed.

Validation for the write-enable round: **88 passed, 0 failed, 0 skipped**, including all eight saves,
edit/reopen/F1/byte-preservation checks, invalid inputs, session undo/redo, omitted-rank insertion,
mixed writes/dictionary relocation, and bulk repair preview/apply/group undo. API build:
**0 warnings / 0 errors**. No console validation is claimed.

Validation for round 2: **79 plugin tests passed, 0 failed, 0 skipped**, with the artifacts variable
exported in the same command. Full API build: **zero warnings/errors**. Round-2 coverage audits every
catalog record and source pin, unknown native-ID distinctions, ability/card sample joins, full-catalog
paging/search, exact recipe/fact/journal counts, bit preservation, narrow/wide encoding vectors,
corrupt lengths/counts/links, independent reader fallback and byte-identical sample no-op exports.
Existing conformance, preserving-write, F1, inventory paging and undo coverage still passes. Missing-sample behavior
continues to use `SampleSaves`; no absent-sample run is counted as validation here.

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Witcher3/SaveToolkit.Plugins.Witcher3.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

## Open problems

Console testing is outstanding. Character edits do not implement level-up rewards/dependencies.
Higher-level/paragon schemas, absent/new inventory stacks, wider money encodings, additional component
prefixes, native inventory extensions, generic saved handles/arrays, ROTS, opaque fact names/words, RB semantics and
other string encodings need further research and samples. Unsupported data stays preserved and opaque.

## Sources and attribution

Research/sample verification: 2026-10-28. Upstream code was cloned into scratch storage and read as
text; no downloaded editor/script was run. The plugin is independently written bounded format code.

| Dataset / code | Source / author | License / notices | Modifications and UI provenance |
| --- | --- | --- | --- |
| 4,011 item references and 107 abilities/mutations | [Witcher Wiki contributors](https://witcher.fandom.com/wiki/Category:The_Witcher_3_items); each record pins a page revision; 2026-10-28 | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), [sidecar](SaveToolkit.Plugins.Witcher3/ReferencePacks/witcher3.catalog.en/data.source.md), [source index](SaveToolkit.Plugins.Witcher3/ReferencePacks/witcher3.catalog.en/source-index.json), [Fandom policy](https://www.fandom.com/licensing) | Normalized labels/categories, adapted wiki introductions and skill concepts, retained explicit unknown-ID references. Attribution in rich cells, pickers and Sources. Share-alike retained; [methodology](CATALOG.md). |
| 225 Gwent indices, source/copy/missable advice and reference stats; facts framing research | [GwentTracker at d8eab8c](https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/data), Chris and Vishal Gupta | MIT; [catalog notice](GwentCatalog-LICENSE.txt), [project notice](GwentTracker-LICENSE.txt), [sidecar](SaveToolkit.Plugins.Witcher3/ReferencePacks/witcher3.gwent.en/data.source.md) | Expanded explicit aliases, normalized descriptions, separated tracker expectations from legal limits. Source in card cells/catalog/pickers/Sources. |
| Journal GUID/resource/status and facts research | [w3-quest-tracker format notes, 6078f8f](https://github.com/dodojesuslol/w3-quest-tracker/blob/6078f8f64a1ede3a304452bfe3cff710ae1d554e/docs/save-format.md), dodojesuslol | MIT; [notice](QuestTracker-LICENSE.txt) | Independent bounded PS5 readers; save-native provenance. No PC compatibility or completion heuristics adopted. |
| SAV3 framing/primitive layout adaptation | [Atvaark/W3SavegameEditor](https://github.com/Atvaark/W3SavegameEditor/tree/cff905189d565606aed9058b2804eb4c5ee9f540/W3SavegameEditor.Core) | MIT, [notice](W3SavegameEditor-LICENSE.txt) | Bounded parser and preserving writer; stored names have save-native provenance. |
| Native inventory/tagged property research and layout adaptation | [cahian converter](https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter/tree/7ae8b6fe71c4b36e571246ad25629d03c7ec8219/w3save) | MIT, [notice](SaveConverter-LICENSE.txt) | Independently bounded PS5 readers; retained raw attributes/flags/prefixes; no PC schema conversion. |
| Raw LZ4 codec | [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4), Milosz Krajewski | MIT, existing managed package | No copied source or reference dataset. |
| Native spatial projections and external regional maps | Native saved definitions/map-tag blocks; [witcher3map](https://legacy.witcher3map.com/), untamed0 and contributors | Original projection code GPL-3.0-or-later; existing friendly labels retain CC BY-SA 3.0. Community map is CC BY-NC-SA, **link only**; art belongs to CD PROJEKT RED. [Sidecar](SaveToolkit.Plugins.Witcher3/SpatialViews.source.md) | No map art, coordinates, community marker data or code imported. Source attribution in graph descriptions, field descriptions and every map/source link; upstream in Info.References. |

The following research sources add context; game script mirrors are research-only and are not
redistributed. Their repositories' tool licenses do not grant rights to CD PROJEKT game assets.

- [Atvaark/W3SavegameEditor at cff9051](https://github.com/Atvaark/W3SavegameEditor/tree/cff905189d565606aed9058b2804eb4c5ee9f540/W3SavegameEditor.Core):
  Round 1 framing, SAV3 directories/virtual offsets, BS/VL/OP/SS/SXAP/BLCK/PORP/AVAL and primitives.
  MIT; notice in [W3SavegameEditor-LICENSE.txt](W3SavegameEditor-LICENSE.txt).
- [rfvgyhn/W3SavegameEditor](https://github.com/rfvgyhn/W3SavegameEditor): inspected the reader fork's
  handle/array handling and confirmed it did not provide a player-writing implementation.
- [solusipse `w3_compress.c` at d685af3](https://github.com/solusipse/witcher3-inventory-editor/blob/d685af31b8a9fcbbf17895874699440418878538/w3_compress.c):
  research reference for 1 MiB raw LZ4 chunks, recomputed sizes/end offsets and absence of a checksum step.
  Its buffer/count/padding assumptions were not adopted; no C source was copied or executed.
- [Cahian converter at 7ae8b6f](https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter/tree/7ae8b6fe71c4b36e571246ad25629d03c7ec8219/w3save):
  `container.py` for preserving-format research, raw-block fallback, 1 MiB sizes and no checksum;
  `skills.py` for tagged structure boundaries/defaults and point-array ordering; `inventory.py` for
  native item layout, quantity width, component hash and equipment flag. Verified against PS5 bytes
  rather than adopting its scans or PC-version assumptions. MIT notice retained in
  [SaveConverter-LICENSE.txt](SaveConverter-LICENSE.txt).
- [Game level-manager script mirror at a85e52c](https://github.com/Lknechtli/BetterCombatEvolved/blob/a85e52c3e3841863999b45afc2a0fd1f284a6bc0/Witcher3/content/scripts/game/gameplay/leveling/levelManager.ws):
  saved `level`/`points`, free versus used points, and level-up side effects. This is a modded older
  script reference, not proof of unchanged next-gen behavior. No script source is copied.
- [Inventory script mirror](https://github.com/Lknechtli/BetterCombatEvolved/blob/a85e52c3e3841863999b45afc2a0fd1f284a6bc0/Extra%20Scripts/content/scripts/game/components/inventoryComponent.ws):
  `GetMoney()` uses the quantity of `Crowns`; `SetMoney()` adds the same named item.
- [Ecostack Switch/PC converter](https://github.com/Ecostack/witcher3-switch-pc-save-converter): `.req`
  content-list semantics and `content0;content1;content2;`, independently confirmed in all PS5 sidecars.
- [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md): raw block
  semantics; no frame or checksum wrapper.
- [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4): managed raw codec,
  version 1.3.8 from the existing central package list, MIT.
- [Repository reconnaissance](../../docs/formats/PPSA04018.md): initial PS5 dimensions,
  independently rechecked against every supplied gameplay record.

## SDK requests

No blocking SDK request for the shipped scope. F12 [#32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32)
resolves the earlier request for bounded F8 traversal and native CLI decoder registration: the
SDK-only production hook is registered explicitly, and F1 tests use its shared host adapter.
F13 [#33](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/33) resolves read-only inspections,
browse-only ability pickers and declared catalogue filtering/sorting. No plugin-local framework
substitute remains. F5 still needs a safe native structural writer; F7 has no proven coupled-file
invariant. Neither is an SDK blocker.
