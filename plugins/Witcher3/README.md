# The Witcher 3: Wild Hunt – Complete Edition (`witcher3`)

Title: **PPSA04018**. Version: **0.4.0**. Maturity: **Experimental**.
Supports player level, unspent XP, available ability points, Crowns and inventory quantity edits for the
supplied PS5 SAV3 schema **64 / 23 / 163**. The preserving writer passes edit conformance and sample
round trips; **edited saves have not been loaded on a console**. When the outer parser recognizes the
container/directory, other SAV3 versions and saves without an unambiguous player identity retain the
read-only explorer; arbitrary future formats are not guaranteed to open. Stable requires human in-game testing.

Round 2 replaces the 18-item starter pack with **4,011 item references, 107 abilities/mutations and
225 Gwent indices**, with revision-pinned CC BY-SA attribution and retained MIT notices. It adds
full-catalog browsing, named saved ability/card joins, community Gwent acquisition/copy advice, and
read-only recipes, journal paths and facts. See [CATALOG.md](CATALOG.md) for coverage and licensing;
[EVIDENCE.md](EVIDENCE.md) records sample evidence and remaining gates. The Phase 1 rich-read slice
is implemented for supplied fixtures; broader compatibility metadata still needs owner captures.
Phases 2–4 remain partial. No new gameplay write class or bulk action is enabled.

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
their quantity bytes. Progression edits preserve the manager handle header, property order, unknown
properties, level definitions and unedited point fields. An absent `free` field is inserted before its
structure terminator; if needed, `free` is appended to MANU without changing existing name indices.
It then updates affected PORP/BLCK/SS lengths, containing variable spans, all shifted variable and RB
offsets, the NM/RB pointers before SC, and the SE variable-table pointer. **Stored variable-table order**
is preserved, even when it differs from physical order. Opaque RB words, MANU metadata, unknown blocks,
padding and trailers remain byte-identical; relocated blocks keep their exact contents.

No-op serialization and undo back to the original values return a copy of the **entire original file**,
without recompression or canonicalization. After any edit, `Serialize()` decompresses its own output,
requires equality with the edited body, re-parses the directories/tree/player, and compares progression
and every inventory ID/name/quantity against the current model. It also verifies flags, durability,
slots, opaque prefixes, prefix names, attributes and tags are unchanged. Failure throws before returning output.

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
sorting and deterministic instance-ID tie breaks. Pages contain at most 200 rows; raw/ability/Gwent
lists page 64 ability/Gwent entries or 128 raw entries. Chunk tables are bounded to 4096 rows. Existing quantity IDs remain
`inventory/<instance>/quantity`, including the Crowns summary; raw explorer IDs use input offsets.

F3 detail cells show raw durability (including -1), saved flags/slot, unknown prefix data, raw
attribute values/type bytes and tags. Categories and labels do not establish safe write limits or
ordinary equipment: internal appearance and scabbard records remain visible. F4's **Look up catalog
item** picker browses the pinned `items` catalog and returns attributed descriptions without modifying
the save. Replacement, equipping, add/remove and refill actions are deliberately absent.

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
and lengths; no skill/stat/mutation edits are enabled.

The Gwent reader validates declared ordinary/leader collection counts and each bounded pair, stops at
the manager's `SBSelectedDeckIndex` boundary and excludes deck nodes. All samples contain 27 indices /
35 copies, including 4 leaders. All observed indices join the catalog; valid empty collections work.
The tracker distinguishes saved copies from community copy expectations and acquisition/missable
advice. Aliases remain separate native indices. Not recorded means availability unknown; deck contents,
actual quest cutoffs and achievement eligibility remain unresolved. No grant/deck/count edits exist.

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

The raw explorer remains read-only and shows the **input image**, while Player/Inventory show pending
edits. It decodes scalar bool/integer/finite float/double, CName, GUID and validated short ASCII strings.
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
these limits and the passing preserving-write/conformance tests.

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

No blocking SDK request for the shipped scope: F2, F3, F4 and F8 are used directly. R6–R9 from the
older plan are no longer blockers for these reads. F5/F6 remain available for later evidence-backed
structural/bulk edits; no plugin-local substitute was added.

Nonblocking F1 tooling request: the CLI's `PluginViewDecoder` visits eager fields/lazy sections but
does not enumerate F8 queryable collections or register plugin-specific decoder hooks. This round's
test-side `WitcherSemanticDecoder` uses F1's actual `ISaveDecoder`/`SemanticDiffer` contracts to compare
native identities without raw-offset explorer noise. A CLI hook registry or bounded F8 visitor would
make that route directly selectable in `savediff compare`. No framework files were changed.
