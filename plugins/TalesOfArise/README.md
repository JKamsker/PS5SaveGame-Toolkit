# Tales of Arise (`tales-of-arise`)

Supports **PPSA02745 (EU), base-game gameplay saves** with the measured 760,856-byte,
30-section PS5 schema. Maturity: **Experimental**. Encryption, integrity and editing
conformance pass against the supplied saves; no edited output has been loaded in-game.
Other regions, PC/PS4 input, Beyond the Dawn and different section versions are not claimed.

## What works

- Opens `SaveData_NNN.sav` by title, filename, length, magic and PS5 mode. Tests cover
  slots 000–002 and all three console backup twins.
- Verifies SHA1 before decryption; follows and validates the shuffled section chain;
  rejects bad alignment, bounds, overlap, cycles, counts, IDs, sizes and versions.
- Curated gald, six status blocks' levels/current HP/SP, existing recognised consumable
  quantities, and read-only play time, experience and base HP. All gameplay meanings
  carry **Inferred** confidence for the PS5 mapping.
- Party stats and character table, one lazy F8 inventory table, lazy read-only explorer of
  all 30 sections / 138,832 primitive uint32 words, and paged plaintext/outer hex views.
  Primitive pages contain at most 256 rows. First-view timings are diagnostic;
  see the dated [performance measurements](PERFORMANCE.md).
- Serializes fixed-size patches, re-encrypts in chain order, recomputes SHA1, then
  decrypts and validates the output and compares every plaintext byte after the header.
  Even no-op serialization runs the actual cipher and is byte-identical on all six files.
- Preserves the entire outer region `0..0x32057`, including screenshot/header bytes.
  Unknown fields, record flags, IDs, padding and unvisited bytes remain unchanged in plaintext.
- Ships the `talesofarise.catalog` F2 pack for `PPSA02745` (English, **v3.0.0**):
  **2,157 records from 62 pinned Aselia revisions**, with CC BY-SA 3.0 share-alike,
  per-record source links and attributed facts/descriptions. Coverage is every named
  row in the listed sources; symbolic keys do not claim PS5 identity or DLC support.
  The 13 existing numeric consumable mappings remain Inferred; only Apple Gel has
  the current PS5 acquisition observation. Inventory IDs stay read-only references;
  recognised existing quantities have bounded writes described below.
- F8 catalog pages search facts/prerequisites as well as names/IDs, filter category,
  character and title, and sort deterministically across the **whole** catalog.
  Pages contain at most 200 rows; filtering does not truncate after the first 200.
- Read-only character sheets show the existing inferred level/HP/XP/SP/base-HP fields.
  Recruitment, equipped gear and skill state remain Unknown. Two unmapped status
  blocks have read-only numeric cells with Unknown meanings in the Party table and
  raw evidence in the character sheets. Read-only aliases stay current after supported edits.
- Arte/skill views show published requirements, costs and effects for 309 character-
  specific artes, 64 skill definitions, 108 title panels and 540 individual nodes.
  Usage, proficiency, title unlock, purchase and panel-completion joins are Unknown.
- Progress views combine 128 recipe/fish/artifact guide records with separate raw
  section evidence for owls, quests/scenario, fishing, camps/maps, PartyProfile and
  ItemManager. They do not infer ownership, learned/cooked, caught/rewarded, found/
  reported/rewarded, effect toggles or completion percentages. Bulk actions only set
  bounded existing-row quantities or restore reductions to their opening quantities,
  using F6 planning and grouped undo.
- F9 adds six bounded skill-panel graphs and six arte-prerequisite graphs. It also adds
  a Dahna artwork background behind the original schematic progress map with attributed external links for 128 guide rows
  plus owl, camp and map domains. Coordinates are plugin-authored grouping positions,
  never claimed in-game coordinates; every saved state remains Unknown.
- Inventory uses one F8 table for all 934 fixed slots, with bounded editable quantity
  pages and whole-inventory search/filter/sort. Zero-ID slots are hidden by default
  and can be shown as "Empty slot" with their raw values intact. Core resolves filtered
  inventory queries into stable fixed-slot row keys for the F6 quantity preview.
- Records section-relative evidence in `Resources/EvidenceLedger.csv`. `AriseDiff` aligns
  decrypted bodies by schema ID and reports relative changed offsets, so shuffled physical
  addresses do not become false field evidence. No new gameplay meaning is promoted by this ledger.
- Publishes the SDK-only `tales-of-arise.sections-v1` decoder hook for the shared SaveDiff
  CLI. Inspection actions explicitly declare `Mutates = false`; read-only item, arte and
  skill identities open browse-only reference pickers with category filters and sorting.

`SystemSaveData.sav` is a separate **4,096-byte opaque system blob**. It is not claimed,
decrypted or rewritten; the framework's raw viewer remains available. Each document
only returns its own file, without silently changing the console backup twin.

## Framework feature adoption

| Feature | Status | Concrete adoption or reason |
| --- | --- | --- |
| F1 SaveDiff | Used | `AriseDiff` aligns decrypted sections by schema ID and relative offsets; twin and slot tests prove the alignment. The shared `PluginViewDecoder` can traverse the plugin without plugin I/O; private sample bytes stay outside git. |
| F2 reference packs | Used | The embedded `talesofarise.catalog` v3 pack is opened through `IReferenceContext`; dataset and per-record attribution appear in catalog descriptions and the Sources panel. SDK-05 `ReferenceViews.Icon` uses `AssetIdOnly` and exact icon-role/publisher alt text with the em-dash fallback, preserving inventory/catalog nodes while resolving image Sources through the pinned context. Without a matching pinned pack, catalog sections show an unavailable message, catalog queries are empty, local inferred labels and raw IDs remain available, and the map omits its background. |
| F3 rich table cells | Used | Catalog and inventory rows expose read-only inspect actions, detail/provenance text and confidence badges. Local F2 icons appear in catalog and inventory image cells, with shared category symbols labelled explicitly. |
| F4 reference pickers | Used | Inventory ID fields and all catalog identity columns use read-only `ReferenceSpec` browse-only pickers, preserve unknown numeric IDs and allow custom fallback. Standalone inspection parameters select an existing reference without changing saved IDs. |
| F5 structural edits | N/A | No safe add/remove/reorder writer exists: allocation, inverse records and acquisition invariants are unproven, so fixed-slot scalar writes remain the only structural-safe scope. |
| F6 planned actions | Used | `inventory/set-quantity` and `inventory/restore-imported` preview changes and skip unsupported rows. Restoration raises only quantities below their immutable opening values, with imported/current quantities both in 0–15. Shared `ActionPlans.ApplyScalar` replans and compares the preview, binds only changed scalar fields and merges application/plan warnings with `ActionPlanWarningPolicy.Merge`. Document, explicit and Core-resolved F8 query scopes are tested; each batch records one undo group. |
| F7 document groups | N/A | `SystemSaveData.sav` is opaque and the console backup twin has no proven independent ownership contract; this plugin intentionally edits one selected gameplay file only. |
| F8 queryable collections | Used | Catalogs page and sort all 2,157 records; the inventory collection supplies stable fixed-slot rows for search and query-scoped planning. |
| F9 maps and graphs | Used | Skill-panel and arte prerequisite DAGs plus an attributed Dahna background behind the schematic map use bounded `GraphNode`/`MapNode` views, text alternatives, stable targets and spatial validation. |
| F10 exact numbers / nullable unions | N/A | The supported PS5 schema is fixed-width integers and bytes; it contains no JSON numeric lexemes or nullable/union fields requiring F10. |
| F11 shared codecs | N/A | Tales of Arise uses its measured AES/XOR/SHA1 format; it has no Kraken or SQLite payload, so `SaveToolkit.Codecs` does not apply. |
| F12 SaveDiff F8 traversal and decoder hooks | Used | `TalesOfArisePlugin` registers `AriseDiff` through `ISaveDecoderProvider` as `tales-of-arise.sections-v1`; section-relative uint32 evidence has stable schema identities, explicit bounds and no Testing dependency. The shared view decoder also traverses rich F8 evidence with explicit test budgets. |
| F13 inspect/picker/filter and float-duration improvements | Used | Shared `ReferenceInspection.Create` declares `Mutates = false` and builds the attributed picker; `ResolveSelection` enforces pinned membership and fixed-row identity on fresh inspection documents. Read-only identities retain browse-only `ReferenceSpec` pickers. Standard category filtering and declared Key/Name/Category sorting delegate to F2; rich facts/character/title filters retain plugin policy. Float durations are N/A: only play seconds use integer duration formatting; play ticks remain a labelled integer counter. |
| F14 empty inventory slots | Used | One F8 inventory table retains all 934 fixed slots; zero-ID rows carry `IsEmpty`, matching `EmptyRows` metadata and default hiding. Queries filter before visibility/paging and report matching hidden counts. F6 query scopes preserve visibility/exclusions and skip empty rows even when shown. |

Among the extracted SDK helpers, this plugin uses the action-plan, reference-inspection and icon
helpers above. Collections, linked binary traversal and catalog enumeration retain their local
implementations; `InMemoryCollection`, the binary cursor/range helpers and
`ReferenceCatalogReader.ReadBounded` are not used. `AriseAssets.Icon` supplies its exact
alt text, publisher notice and asset-availability policy through `ReferenceIconOptions`
(see [SDK requests](#sdk-requests)). These are implementation choices, not missing SDK APIs.

## Publisher assets (2026-10-29)

The v3 F2 pack adds **110 unique icons**, **25 official-description records**
(16 distinct localized arte transcriptions: 18 playable arte rows and seven matching
skill nodes), and **one Dahna world-map background**. Icon coverage is 271 items,
675 equipment, 64 skills, 59 artes, 463 skill nodes and 128 progress guides: **1,660
records** in total. Item/equipment/progress artwork uses the game's shared category
symbols; the 59 arte matches cover named Alphen/Rinwell guide rows. Unmatched artes
keep their text fallback. This adds reference content without changing save identities,
Unknown states, existing writer bounds or Experimental warnings.

**New embedded image bytes: 185,302** (90,698 icons + 94,604 map), below the 4 MB
budget. Icons are at most 64×64, using lossless WebP or smaller PNG-8. The map is
1200×675 WebP. The background is decorative: markers retain original schematic
positions, scaled by (12, 6.75) to match display bounds; they are **not geographic
locations**. Alignment remains unconfirmed and no progress state is inferred. The
same schematic and text list remain usable when the optional background is absent.

The exact download/output hashes, dimensions, record mappings and description
response hashes are in the minified [asset ledger](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/asset-sources.json).
[Asset attribution and all source URLs](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/assets.source.md)
cover publisher **Bandai Namco Entertainment Inc.**, Aselia Wiki contributors,
Gamer Guides and the Tales terminology dictionary (atwiki). Media licence field:
**Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29**.
Aselia adapted text/transcriptions retain **CC BY-SA 3.0**; official wording is
identified as a wiki transcription, not an independently verified game dump.
Attribution appears in `Info.References`, catalog inspection/details, the map's
provenance and the framework Sources panel; image alt text includes the publisher
notice and icon role. These media permissions
resolve this plugin's former publisher-art exclusion; save-state joins still need evidence.

Historical asset-overlay stopwatch (2026-10-29), Release build, `SaveData_002.sav`
(760,856 bytes; all six gameplay samples have the same size),
with sample import excluded: cold pack load + pin + open + initial view changed from
**113.76 ms / 22,432,928 allocated bytes** to **177.71 ms / 30,514,600 bytes**.
A second document using the same pinned pack changed from **12.23 ms / 6,179,920 bytes**
to **13.28 ms / 6,210,128 bytes**. These are indicative single-process measurements,
including first-use JIT in the cold case, not a throughput benchmark. Framework pack
loading remains once per store; documents reuse the same immutable image buffers.
The plugin performs no runtime downloads or image decoding. Processing occurred under
`%TEMP%` with `python -I`, Pillow 12.2.0, header validation and pinned SHA-256 checks.

The asset round reported byte-identical overlay regeneration and an independent
JSON comparison preserving every original fact and identity across all 2,157 records.
These are historical results; this documentation pass does not rerun the downloads.
Current local validation is listed under [Validation](#validation).

## Enabled writes and their evidence

All rows below remain **Experimental** and retain visible warnings. The PS5 field
meanings are **Inferred** unless stated otherwise; the owner must complete the linked
[owner validation tracking issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
before promoting any feature or plugin to Stable.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| PartyProfile gald (`party/gald`) | Inferred | Body `+0x18` matches the cited PS4 mapping; fixed-width samples and atomic edit/reopen tests. All supplied samples are zero, so no in-game value change is proven. | Known ciphertext SHA1 is recomputed; AES/XOR re-encryption reparses and compares plaintext. | Buy and consume one item, then save/reload and test normal acquisition; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| Level, current HP and SP in status blocks 000–005 with nonzero level | Inferred | Body offsets match the cited PS4/PC layout and sample values; edit/reopen and unrelated-byte preservation tests. Nonzero level permits these bindings; it does not prove recruitment or active-party membership. Level dependencies and effective stats are intentionally untouched. | Known ciphertext SHA1 is recomputed and self-checked; load-screen preview and dependent caches are preserved. | Load a spare edited slot, verify menus, perform a normal level/HP/SP event, save/reload; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| Existing recognised consumable quantity (IDs 1–13, current and target quantity 0–15) | Inferred | 13 legacy numeric mappings; Apple Gel is observed once in slot 002. Fixed record offsets, flags/IDs/order preservation, range rejection and edit/reopen tests. Existing quantities above 15 are read-only. | Known ciphertext SHA1 is recomputed and self-checked; no records, IDs, flags or padding are changed. | Buy/use an item, test zero and later acquisition, and validate normal/effective stack caps; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| `inventory/set-quantity` F6 action over explicit, document or Core-resolved inventory-query scope | Inferred | Deterministic preview, skipped empty/unknown/over-bound/unchanged rows, bounded target 0–15, apply-time revalidation and one grouped undo. Document/explicit scopes require inventory expansion; query scope supplies resolved fixed-slot keys. No record creation or grant behavior. | Same known SHA1/AES self-check as scalar edits; the game may reject inferred semantics. | Restore a spare slot, apply a preview, verify the effect and later acquisition after reload; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| `inventory/restore-imported` F6 action over the same scopes | Inferred | Captures an isolated inventory baseline when the document opens, before any staged edit or first preview. Only existing recognised IDs 1–13 whose imported/current quantities are both 0–15 and whose current quantity is lower are restored. Raised, equal, unknown/equipment, empty and over-bound rows are skipped. Synthetic coverage exercises all 13 IDs and boundaries; the observed Apple Gel in slot 002 and its backup cover preview, apply, grouped undo/redo, query exclusions and derived packed-word effects. | Same ciphertext SHA1 and AES/XOR re-encryption self-check; quantity-only changes preserve every unrelated byte and the outer preview. | Buy/use/zero/reacquire each eligible ID on a spare slot, restore a staged reduction, consume after editing, save and reload; inferred caps/mappings and console acceptance remain unvalidated under [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |

“Restore imported” uses this workspace's immutable opening quantities, not a game
maximum, the value before the last edit or a historical balance lost before import.
Quantities already above the baseline stay unchanged. Re-importing an edited save
establishes a new baseline. The action accepts no quantity parameter and uses the
same preview/revalidation, atomic scalar application and undo/redo path as setting
quantities. Dismissing a preview makes no changes. It adds no item mappings or cap
claims beyond the [legacy mapping evidence](SaveToolkit.Plugins.TalesOfArise/Resources/Consumables.source.md).

Equipment replacement, item insertion/removal, structural changes, HP/CP restore,
growth/XP edits, skill/title/arte writes and progression/completion mutations remain
disabled because enabling them could corrupt unproven identity or dependency state even
with a warning.

## Container layout and reconciled offsets

All integers are little-endian. File offsets below are absolute.

| Offset | Length | Meaning / validation |
| --- | ---: | --- |
| `0` | `0x32058` | Opaque outer header and screenshot, preserved verbatim |
| `0x32058` | 4 | Magic `30 16 05 10` |
| `0x3205C` | 4 | Gameplay block length `0x87BC0`, measured **from magic to EOF** |
| `0x32060` | 20 | SHA1 of the final ciphertext at `0x32078..EOF` |
| `0x32074` | 1 | Platform/XOR mode `03` (PS5) |
| `0x32075` | 1 | Regular algorithm selector `01`; alternatives rejected |
| `0x32076` | 2 | Preserved alignment bytes |
| `0x32078` | `0x40` | First encrypted block: 16 relative uint32 offsets |

EOF is `0xB9C18`. Actual ciphertext length is `0x87BA0`, not the length word.
The upstream prose offset `0x32068` for the mode overlaps its own SHA1 range and is
incorrect for these saves. The converter's computed constants correctly place it at
`0x32074`. Another prose arithmetic typo, `0x32078 + 0x18 = 0x32000`, should be
`0x32090`. The PS5 directory pointer is at byte `0x18` within the first decrypted
block, i.e. file offset `0x32090`. All pointer targets are relative to `0x32058`.

That pointer locates a 64-byte directory with marker 100 at `+0`, count 30 at `+4`,
and first section pointer at `+8`. Each section has a separately encrypted 16-byte
header: version at `+0`, identifier at `+4`, padded body size at `+8`, next header
pointer at `+0xC`; its encrypted body follows immediately. The final next pointer is zero.
The section names/order follow upstream research; embedded `Resources/Sections.csv`
records the IDs, versions and sizes confirmed across this corpus. IDs stay constant
while bodies change, so they are treated as schema identifiers, **not body checksums**.
Their name-hash algorithm is unknown.

Physical directory offsets relative to the block are `0x83F50`, `0x5BD70`, and
`0x7E770` for slots 000, 001, and 002 respectively. No gameplay field uses a fixed
absolute file address. Unsupported schema changes fail closed instead of guessing.

## Encryption and integrity

An independently written C# implementation uses .NET AES-256-ECB, no padding,
and the public PS5 64-byte key material and XOR table documented by save-converters.
Only the first 32 key bytes configure AES. The full 64-byte key evolves as follows:

1. Process the first 64-byte offset block, then the pointed-to 64-byte directory,
   then each 16-byte section header and its complete body, in linked order.
2. Within each such region, process chunks of up to 64 bytes. Before AES encryption
   XOR plaintext with feedback, swapping adjacent feedback bytes (`i ^ 1`) for PS5.
   Decryption applies AES first, then that same XOR.
3. Replace the feedback prefix with the chunk's ciphertext, preserving unused feedback
   bytes for a short chunk. Feedback persists across headers and bodies.
4. After each complete region, SHA1 its **plaintext** and XOR the repeating 20-byte
   digest into all 64 key bytes. A zero XOR result retains the old nonzero byte;
   if the old byte was zero too, use `0x20`.
5. After all encryption, SHA1 the **entire physical ciphertext**, from `0x32078` to
   EOF, in file order. Write its raw 20-byte digest at `0x32060`.

Thus the internal key schedule hashes plaintext in traversal order, whereas the
stored integrity digest hashes ciphertext in physical order. Both distinctions are
verified by exact re-encryption of every supplied gameplay file. The stored digest
matches ciphertext on 6/6 records and does not match the corresponding plaintext.
No compression or additional integrity field is identified in this schema.
SHA1 is retained solely for compatibility with the game's format.

## Curated fields: evidence and limits

Offsets here are relative to each **section body**, after its 16-byte header.
The PS4 patch codes locate the section ID at header `+4`, so their relative offsets
must be reduced by `0xC` to address the body. This is separate from the encryption
offset discrepancy above.

| Section | Offset | Field | Access / evidence |
| --- | --- | --- | --- |
| PartyProfile | `0x18` | Gald, uint32 | Editable 0–999,999,999; source ID +`0x24`; zero in all supplied slots |
| PartyProfile | `0xAF8` | Play seconds, uint32 | Read-only; equals outer header `+8` in every slot |
| PartyProfile | `0x2000` | Play ticks, uint32 | Read-only; `/60` floors to seconds; duplicate at `0x2008` preserved |
| ArisePCStatus_000..005 | `0x04` | Level, uint32 | Editable 1–100; PS4 source ID +`0x10`, all six are level 5 |
| same | `0x08` | Current HP, uint32 | Editable 0–99,999; PC table layout minus `0x40` fits PS5; inferred |
| same | `0x10` | Experience, uint32 | Read-only; PS4 source ID +`0x1C` |
| same | `0x30` | SP, uint32 | Editable 0–999,999; PS4 source ID +`0x3C`; consistent PC layout |
| same | `0x50` | Base HP, uint32 | Read-only; PS4 source ID +`0x5C` |
| SaveData_ItemManager | `i * 16` | Stack quantity byte | Editable only for existing IDs 1–13 whose current quantity is 0–15; targets are 0–15. F6 accepts explicit, document and Core-resolved query scopes. |
| same | `i * 16 + 1` | Flags byte | Raw, preserved |
| same | `i * 16 + 2` | Item ID, uint16 | Read-only; 934-record scan from cited PS4 patch |

Character names and the 13 consumable IDs are embedded reference tables; unknown
item IDs retain numeric labels. Zero-ID records are retained as empty slots and hidden
by default; there is no item
insertion, flag editing, accessory editing or bulk grant action. Existing values
outside the supported editing guardrails remain visible but read-only. Character
level/HP/SP bindings also require a nonzero level in status blocks 000–005;
the checked scalar must already be within its supported range. This gate does not
establish recruitment or active-party membership. Indices 6/7 of the
character sections are unmapped and read-only, not assumed to be active party members.
Their scalar meanings, and quantities attached to empty or unrecognised item IDs,
are labelled **Unknown**. Recognised consumable quantities retain **Inferred** confidence.

**Verified** means the byte-level observations, cipher, integrity coverage and
round-trip preservation described above. Curated PS5 **meanings remain Inferred**:
the cited PS4/PC offsets and sample consistency support them, but no controlled
in-game PS5 edit has confirmed them. Explorer words have **Unknown** semantics.
Numeric maxima are editing guardrails, not claims of validated PS5 game limits.

Level edits do not synthesize experience, recalculate stats, or change the outer
load-screen preview. Current HP need not equal base HP once equipment/bonuses are
involved. Play time is read-only because it has duplicate counters and an outer
copy which this plugin deliberately preserves. Bulk inventory codes in the source
have reports of broken accessory/key-item acquisition; this plugin does not implement them.

Scalar edits report the old and new values of all published read-only numeric views
over the same bytes, including character sheets, Explorer words and queried F8
evidence. Undo/redo refresh those values while writing only the editable fields.
F6 quantity previews include exposed packed-word effects as derived changes; they
preserve the adjacent ID/flag bytes and never make those words writable. Preview
calculation leaves the save unchanged, and changed effects, warnings or parameters
require a fresh preview.

### Measured samples

Primary and backup are identical for each slot; these are three different saves,
not six independent play sessions. No repeated ZIP path generations exist here.

| Slot | Seconds / ticks | Experience (six main characters) | Inventory records |
| --- | --- | --- | ---: |
| 000 | 1,890 / 113,431 | 0 | 24 |
| 001 | 2,770 / 166,253 | 33 | 24 |
| 002 | 3,166 / 189,968 | 33 | 25 |

Base/current HP are 321, 282, 255, 291, 339, 312 for character indices 0–5 in all
three slots. Slot 002 adds an Apple Gel stack of one at inventory record 24.

### F14 inventory audit

Previously the eager inventory omitted empty records while a separate read-only F8
collection covered the same occupied slots. Both are replaced by one F8 table with
editable recognised quantities. Every slot retains its `inventory/<index>/quantity`
row key and original physical offset. The six sample files have 934 slots each:
slots 000/001 contain 24 nonzero IDs and 910 empty slots; slot 002 contains 25 nonzero
IDs and 909 empty slots. Tests independently check that every zero-ID sample record
is entirely zero-filled across its 16 bytes. This retains the existing zero-ID
empty-record rule; zero quantity alone and unknown nonzero IDs never hide a row.
For other saves the marker follows the raw zero ID without rewriting residual bytes.
The supported PS5 inventory layout and field meanings remain Inferred, based on the
[existing PS4 research](https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt)
and sample observations; this presentation change makes no new console-qualified claim.

The eager eight-character table, reference catalogues, F8 section-evidence tables
and paged primitive explorer were also audited. They have no confirmed vacant-slot
semantics, so zero words and unmapped characters remain visible. Explorer byte pages
are raw-format inspection, not inventory slot groups; no "Slots N–M" sections exist.

F6 document scope continues to select occupied records only. Query scope selects all
matching visible slots across unloaded pages, retains explicit visibility/exclusions,
and skips empty rows if shown. Direct page edits, grouped actions, undo/redo and
byte-identical no-op serialization retain the original slot identities.

## Validation

From the repository root, with the private sample archive available in the external
task scratch's `samples/` directory:

```powershell
$taskScratch = 'D:/Temp/saveeditor/test-scratch/coverage-talesofarise'
New-Item -ItemType Directory -Force -Path $taskScratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = "$taskScratch/samples"
$env:SAVETOOLKIT_TEST_TEMP = $taskScratch
$env:TEMP = $taskScratch
$env:TMP = $taskScratch
$env:TMPDIR = $taskScratch
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
dotnet test --project plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests -c Release
python -I -X utf8 plugins/TalesOfArise/ReferenceData/test_wiki_tables.py
python docs/tests/test_documentation.py
```

Documentation accuracy check, **2026-10-29**: the full Release rerun passed
**347 tests, 0 failed, 0 skipped** with samples. The first full run had one
intermittent allocation-guard failure in
`CipherSetupAllocationsDoNotGrowWithTheNumberOfFeedbackChunks`; the unchanged
full rerun passed. No runtime code, test, view string, ID or confidence label
was changed by this documentation pass.
API Release build with warnings as errors: **0 warnings, 0 errors**;
documentation checks: **14 passed**; Python table-parser checks: **3 passed**.

SDK-05 adoption, **2026-10-29**: the pre-migration suite passed **365 tests**;
the final Release suite passed **366 tests, 0 failed, 0 skipped**, including
17 unchanged icon/view characterization cases and a new pinned-image Sources check.
Catalog and all three sample view/inventory fingerprints stayed identical.
The first post-migration run hit the same intermittent cipher-allocation guard
documented above; the full unchanged rerun passed. API Release build: **0 warnings,
0 errors**; offline Python checks: **18 passed**; documentation checks: **14 passed**.
Samples, test scratch, TEMP/TMP/TMPDIR, logs and TRX reports were under
`D:/Temp/saveeditor/test-scratch/adopt-sdk05-tales`.

Edit-coverage addition (#40), **2026-10-29**: Release suite **383 passed,
0 failed, 0 skipped** with the sample archive and all scratch on D:. Seventeen new
restore cases cover immutable opening values, all 13 recognised IDs, imported and
current bounds, raised quantities, unsupported rows, dismissed/altered/stale previews,
query exclusions, derived aliases, re-import baselines and multi-row grouped undo/redo.
All 147 generated corruption cases and existing allocation guards remain green;
new restore guards retain the 1 MiB planning and 512 KiB single-row apply bounds.
The three existing sample-view fingerprints remain identical after removing only
the separately asserted new action node. API Release build with `--warnaserror`:
**0 warnings, 0 errors**. `git diff --check` passed.
The repository documentation check (`--code-ref main`) ran **27 tests: 26 passed,
1 failed**: `test_built_app_smoke_docs_cover_shipped_flows_and_skipped_regressions`
still expects `test.fixme(` in `web/e2e/import.e2e.ts`, although the referenced test
is enabled. Both inputs are unchanged from this branch's starting commit; correcting
that baseline mismatch is outside this plugin's scope.

The suite covers independent chained cipher vectors and SHA1 `abc`, pinned
whole-plaintext SHA256 digests, all three gameplay slots and their backup twins,
byte-identical no-op serialization, known sample values, atomic edits, reopen,
unrelated-byte preservation, numeric aliases and grouped undo/redo. Sample tests
use `SampleSaves` and skip when the archive is absent; a run without samples is
not evidence that the sample-backed checks passed.

Authored in-memory containers exercise normal and physically reversed section
chains. Corruption tests cover envelope cuts, every partial AES-block length,
repaired-SHA1 structural mutations, directory bounds, section IDs/versions/sizes,
first/middle/last links, overlap, cycles, early termination and unexpected tails.
Malformed fixtures retain an independent captured encryption schedule rather than
using the validating writer. Generated fixtures do not establish gameplay meanings.

Reference/view coverage checks catalog counts and source pins, title/node joins,
unknown identities, full-model search/filter/sort/paging, read-only inspections on
fresh documents, fixed-record selection, missing-pack fallback, bounded spatial
views, every explorer page, decoder budgets and empty-slot visibility. F6 tests
cover scope/diagnostic precedence, quantity boundaries, skipped rows, stale
previews, derived word effects, unloaded bindings and Core-resolved query scopes.
Allocation/call-count guards cover cipher setup, verified serialization, raw evidence
pages and single-row planned apply. Sample correctness has no elapsed-time deadline;
[the dated performance report](PERFORMANCE.md) records diagnostic timings,
baselines and the allocation guards. Its historical test counts are not current totals.

Deferred maintainability work from the 2026-10-29 review:
`ReferenceData/build_catalog.py` (`extract_table` and `build`) and
`ReferenceData/build_assets.py` (`build`) exceed 50 lines. These offline utilities
retain their output policy until complete pinned-source catalog/asset regeneration
has golden coverage; the Python suite currently covers HTML table parsing. They
are never invoked by the runtime plugin.

### Native SaveDiff evidence

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --decoder tales-of-arise.sections-v1
```

`AriseDiff` retains its byte-offset comparison helper and exposes the same schema alignment
through the F12 SDK hook. Paths are `evidence/<schema-id:X8>/<body-offset:X6>` and values are
invariant little-endian uint32 integers, labelled as unknown meaning. Four-byte words cover
every section body, including unknown bytes; these are section-relative observations, not
claimed gameplay record identities. The validated schema fixes section IDs, sizes and versions.
Reordering a physical section does not alter evidence identity. Outer metadata, screenshots,
link pointers and gaps are excluded and explicitly reported as a decoder warning.

The native hook emits 138,832 values within default F1 budgets, stops immediately when the
sink refuses a value, and reports incomplete evidence for insufficient output/depth budgets.
The generic view decoder additionally emits explorer and rich F8 presentation values; its
full-traversal regression uses larger explicit budgets. Production code depends only on SDK;
tests use the shared `HookSaveDecoder` adapter without a plugin-local workaround.

## Round-2 scope and remaining evidence

The catalog/data portion of phases 1–2 is complete for the pinned source coverage.
The new read-only character, arte/skill and progression evidence views continue phases
2, 4 and 5. They do **not** complete the plan's semantic/save-identity gates: the sample
corpus still has three early-game snapshots, no controlled skill/equipment/progress
pairs, and no console-qualified new writes. Phases 3–6 remain gated by that evidence.

Follow [the exact owner capture/qualification checklist](OWNER_CHECKLIST.md) before
enabling quantity-category expansion, equipment swaps, character growth/CP operations,
skill purchases/refunds, structural inventory or progression actions. The existing-row
quantity action is the only enabled bulk operation; restore-all, unlock-all, completion,
preset and structural mutations remain unavailable. Bulk actions use
`IActionPlanningDocument`/`SupportsPlanning`, apply-time revalidation and one undo group.

## Open questions

- In-game loading and gameplay behavior after PS5 edits; this blocks Stable maturity.
- CP, grade/score, remaining field meanings, equipment, accessories and flags.
- Meaning of duplicated play counters, additional party members, section ID hashing,
  and unknown outer metadata/integrity dependencies. The outer preview is not refreshed.
- Nonzero gald evidence and controlled PS5 inventory/HP/SP changes; supplied saves
  are early-game and do not validate late-game/DLC schemas or upper ranges.
- Alternate platform/algorithm selectors, other save versions and Beyond the Dawn.

## Sources and attribution

Research performed 2026-10-28. Downloaded implementations were read as untrusted
text; none was executed. The algorithm and public constants were independently
implemented in C#; no substantial upstream code was copied. Runtime dependencies
are the SDK and .NET cryptography only, with no added NuGet packages. The owner
decision on 2026-10-28 allows reuse of community facts from sources that publish
no licence, with attribution. Exact hashes and transformations are in
[`UnlicensedSources.source.md`](SaveToolkit.Plugins.TalesOfArise/Resources/UnlicensedSources.source.md).

- [ItsAllAboutTheCode/save-converters notes, revision 6632390](https://github.com/ItsAllAboutTheCode/save-converters/blob/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise/save_NOTES.md):
  PS5 AES/XOR material, section names/order, pointer base, platform differences.
- [Converter implementation, inspected revision 6632390](https://github.com/ItsAllAboutTheCode/save-converters/blob/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise/tales_of_arise_save_converter.py):
  authoritative computed offsets, feedback byte swapping/partial updates, plaintext
  key evolution and final ciphertext SHA1. Upstream is
  [GPL-3.0](https://github.com/ItsAllAboutTheCode/save-converters/blob/main/LICENSE);
  attribution is to the published research, not a claim that copied GPL code is relicensed.
- [PS4 research thread](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070),
  especially [ロード's 2022-04-03 patch/reference attachment](https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt):
  gald and relative character offsets, 934 inventory records, item acquisition warnings.
- [PS4 discussion](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30162),
  posts 30214, 30222, 30223, 30245 and 30246: character IDs, quantity/flags/item ID
  layout and Japanese consumable names (English labels supplied here).
- [Sora3100 Arise.CT, revision c928710](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/c92871084d714d98b3854ba9fbb4e3f022145d7e/Arise.CT):
  character index/name ordering and PC level/HP/experience/SP/base-HP layout.
  PC runtime addresses are not used as save-file addresses.
- [Gald Editor author page](https://www.nexusmods.com/talesofarise/mods/407):
  consulted for existing tools; this is a PC memory table, not PS5 save-layout evidence.
- [Local reconnaissance](../../docs/formats/PPSA02745.md) and
  [method](../../docs/formats/README.md): initial archive measurements and offset discrepancy.
The wiki-derived datasets below are by **Aselia Wiki contributors**, retrieved **2026-10-28**,
and remain **[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)**.
Names and published facts/descriptions were extracted, whitespace normalized, images
and navigation omitted, and symbolic keys added. Unknowns/source duplicates were
retained. Every record has its own revision URL; every source is also in `Info.References`
and UI descriptions/provenance. Dataset attribution appears in the Sources panel.

| Dataset | Coverage | Pinned source / attribution |
| --- | --- | --- |
| Publisher media overlay | 110 icons, 25 description records, one decorative Dahna background; Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29 | [Complete asset sidecar and source ledger](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/assets.source.md), retrieved 2026-10-29; Aselia text retains CC BY-SA 3.0 |
| `items` (271) | All named consumables, materials, accessory materials, valuables, fishing items and artifacts | [Consumables r382800](https://aselia.fandom.com/wiki/ToA_-_Consumables?oldid=382800); all six lists in the [source ledger](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/data.source.md) |
| `equipment` (675) | Six weapon/armor types, accessories, costumes, styles and attachments | [Swords r363915](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Swords)?oldid=363915); every category/character revision in the source ledger |
| `artes` (309) | Six playable and thirteen allied/enemy/cameo character pages; character-specific Boost Strikes | [Alphen artes r378115](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Artes?oldid=378115); all 19 pages and Boost Strike cross-check in the source ledger |
| `skills` (64) | All parameter/action/character skills, published base/DLC stacks, effects and unknowns | [Skills r378099](https://aselia.fandom.com/wiki/ToA_-_Skills?oldid=378099) |
| `titles` (108) | All six characters' 18 panels, requirements and bonuses | [Alphen panels r378024](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Skill_Panels?oldid=378024); six character revisions in the source ledger |
| `skill-nodes` (540) | All five positions per panel, SP text and title prerequisites | Same six pinned panel pages; dash costs remain distinct from explicit zero |
| `characters` (62) | Every named character/group in the navigation template; six inferred playable status joins | [Character template r300086](https://aselia.fandom.com/wiki/Template:ToA_Characters?oldid=300086) |
| `progress` (128) | 45 recipes, 44 fish species, 39 artifacts; guide data, no saved state joins | [Cooking r379806](https://aselia.fandom.com/wiki/ToA_-_Cooking?oldid=379806), [Fishing r350354](https://aselia.fandom.com/wiki/ToA_-_Fishing?oldid=350354), [Artifacts r381166](https://aselia.fandom.com/wiki/ToA_-_Artifacts?oldid=381166) |
| `Characters.csv` and playable status-index attributes | Six short names plus unmapped placeholders and six inferred status-index joins; no recruitment claim | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Characters.source.md), including the no-licence Sora3100 ordering reused with attribution per owner decision 2026-10-28 |
| `Consumables.csv` | 13 legacy inferred numeric mappings; no newly established IDs | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Consumables.source.md), including the no-licence PS4 patch crosswalk reused with attribution per owner decision 2026-10-28 |
| `Sections.csv` | Published section labels plus independently measured schema facts; GPL-3.0 format research, no code port | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Sections.source.md) |
| F9 schematic coordinates and external map links | Original plugin-authored grouping positions over separately attributed Dahna art; no geographic alignment claimed | PS5 Save Game Toolkit contributors, GPL-3.0-or-later, 2026-10-28; [repository](https://github.com/JKamsker/PS5SaveGame-Toolkit). [Tales of Arise guide index](https://aselia.fandom.com/wiki/Tales_of_Arise), [Owls](https://aselia.fandom.com/wiki/ToA_-_Owls), [Camps](https://aselia.fandom.com/wiki/ToA_-_Camps) are external references; background artwork has separate publisher attribution in the asset sidecar and Aselia text remains CC BY-SA 3.0. |

The [data sidecar](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/data.source.md)
lists all **62 immutable revision URLs**, author/licence/date/modifications and coverage
limits. `source-revisions.json` pins rendered response and wikitext SHA-256s; the pack
manifest pins all record files. [Maintenance scripts](ReferenceData/README.md) reproduce
the extraction from hash-checked downloads held outside the repository. The pack also
pins the two reused no-licence community crosswalks in per-record attributes and the
sidecar. The v3 publisher-media overlay has separate attribution in the linked asset sidecar.
Proprietary tools, patch instructions and downloaded implementations are not bundled;
these are implementation/research choices, not recorded licence refusals.
Wiki references include DLC/expansion facts; the supported save schema remains base-game
PPSA02745 only. No complete owl/quest identity table or native save join was established;
this is an evidence gap, not a licence blocker under the owner policy.

## Licence exclusions

No current source-specific licence-based refusal is recorded for this plugin.
Historical exclusions and research-only choices are distinguished below; none
establishes that a named source has GPL-2.0-only or no-redistribution terms.

| Source URL / revision | Exact licence found | Refused material / reason | Alternative used | What would unblock reuse |
| --- | --- | --- | --- | --- |
| [ItsAllAboutTheCode/save-converters, `663239063e7c1cc8550f4248f772ee9dd47ed5b0`](https://github.com/ItsAllAboutTheCode/save-converters/tree/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise), retrieved 2026-10-28 | GPL-3.0, as recorded in the research and [section sidecar](SaveToolkit.Plugins.TalesOfArise/Resources/Sections.source.md); no more specific licence variant is recorded here. | Format research used; converter implementation not copied. This is an implementation choice, not a recorded licence refusal. | Independently written C# AES/XOR/SHA1 implementation and measured section schema. | No licence blocker recorded; any future code port must retain the recorded upstream notices and terms. |
| [ロード, PS4 attachment 31135 (2022-04-03)](https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt), retrieved 2026-10-28; response SHA-256 pinned in the [crosswalk sidecar](SaveToolkit.Plugins.TalesOfArise/Resources/UnlicensedSources.source.md) | No licence published; reused with attribution per owner decision 2026-10-28 | Resolved: 13 legacy numeric consumable mappings imported as Inferred F2 attributes; patch instructions are not redistributed. | Attributed facts and independently implemented bounded quantity writes. | Unblocked by owner decision 2026-10-28; import complete. PS5 mapping validation remains an evidence gap. |
| [Sora3100 `Arise.CT`, `c92871084d714d98b3854ba9fbb4e3f022145d7e`](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/c92871084d714d98b3854ba9fbb4e3f022145d7e/Arise.CT), retrieved 2026-10-28; response SHA-256 pinned in the crosswalk sidecar | No licence published; reused with attribution per owner decision 2026-10-28 | Resolved: six-character ordering imported as Inferred F2 attributes; Cheat Engine addresses/scripts are not copied. | Attributed ordering and independently implemented save-field access. | Unblocked by owner decision 2026-10-28; import complete. PC addresses do not establish PS5 save joins. |
| [PS4 research thread 30070](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070), retrieved 2026-10-28; response SHA-256 pinned in the crosswalk sidecar | No source-specific licence terms recorded. | Research cross-check; no licence refusal recorded. | Cite the thread; import the separately pinned attachment facts above. | No licence blocker recorded. |
| [PS4 item discussion 30162](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30162), retrieved 2026-10-28; response SHA-256 pinned in the crosswalk sidecar | No source-specific licence terms recorded. | Research cross-check for character/item layout and names; no licence refusal recorded. | Attributed, independently implemented field access and crosswalks. | No licence blocker recorded; further PS5 identity joins need evidence. |
| [Gald Editor author page](https://www.nexusmods.com/talesofarise/mods/407), consulted 2026-10-28; no specific revision recorded | No source-specific licence terms recorded. | Research-only PC memory table; not PS5 save-layout evidence. No licence refusal recorded. | Independently implemented save-field access based on the cited format research and samples. | A build-matched save-layout source would address the evidence gap; no licence blocker recorded. |
| [Aselia Wiki contributors, 62 pinned revisions](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/data.source.md), retrieved 2026-10-28 | CC BY-SA 3.0 | Imported attributed facts/adapted descriptions retain CC BY-SA 3.0 and modification notices. No refusal. | Not needed; catalog data is embedded. | Already imported; saved-state joins remain separate evidence gaps. |
| [Aselia Tales of Arise guide index](https://aselia.fandom.com/wiki/Tales_of_Arise), unpinned external map source | CC BY-SA 3.0 text; no media grant recorded. | External reference retained; no guide-index art/coordinate import needed and no licence refusal recorded. | Separately attributed Dahna artwork and original schematic positions. | No licence blocker recorded; no further import needed for the current presentation. |
| [Aselia owls](https://aselia.fandom.com/wiki/ToA_-_Owls), unpinned external map source | CC BY-SA 3.0 text; no media grant recorded. | External reference retained; attempted API title returned `missingtitle`. No licence refusal recorded. | Raw owl-section evidence and original schematic positions; saved states remain Unknown. | Accessible identity data and native save joins are missing. |
| [Aselia camps](https://aselia.fandom.com/wiki/ToA_-_Camps), unpinned external map source | CC BY-SA 3.0 text; no media grant recorded. | External reference retained; no native camp identity/coordinate crosswalk imported. No licence refusal recorded. | Original schematic positions and separately attributed Dahna artwork. | Native camp/world semantics remain Unknown. |
| [Aselia-hosted publisher icons and official-description transcriptions](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/assets.source.md), pinned URLs/hashes in `asset-sources.json`, retrieved 2026-10-29 | Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29. Aselia text/transcriptions retain CC BY-SA 3.0. | Resolved: imported publisher-media overlay with attribution to Bandai Namco Entertainment Inc. and Aselia Wiki contributors. | Unmatched records retain text fallbacks. | Unblocked by owner decision 2026-10-29; documented overlay imported. |
| [Gamer Guides Alphen artes](https://www.gamerguides.com/tales-of-arise/guide/characters/alphen/artes) and [Rinwell artes](https://www.gamerguides.com/tales-of-arise/guide/characters/rinwell/artes), pinned URLs/hashes in the asset ledger, retrieved 2026-10-29 | Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29. Gamer Guides is credited as a redistributor; no separate site licence terms are recorded. | Resolved: publisher artwork imported with publisher and host attribution. | Unmatched artes retain text fallbacks. | Unblocked by owner decision 2026-10-29; documented overlay imported. |
| [Tales terminology dictionary (atwiki), Dahna map](https://w.atwiki.jp/talesofdic/pages/18355.html), pinned URLs/hashes in the asset ledger, retrieved 2026-10-29 | Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29. Atwiki is credited as a redistributor; no separate site licence terms are recorded. | Resolved: decorative Dahna background imported with publisher and host attribution. | Original schematic positions and text list remain available. | Unblocked by owner decision 2026-10-29; import complete. Geographic alignment remains unconfirmed. |
| No specific source/revision recorded (former generic GPL-2.0-only or incompatible-code exclusion) | No source-specific licence terms recorded. The former generic wording does not identify any source with those terms. | No source-specific refusal can be established from that entry. | Retain the independently written implementation and the exact named-source terms above. | Identify a concrete source/revision and its exact terms before recording a licence refusal. |
| No specific source/revision recorded (commercial save-editor tables, other patch instructions and downloaded implementations) | No source-specific licence terms recorded. | Remain research-only; no specific licence-based refusal is recorded. | Attributed format facts, independently implemented code and the documented imports above. | Identify a concrete source/revision, material and terms before assessing reuse. |

## SDK requests

Resolved and adopted: F12 native decoder hooks, F13 read-only inspection and
browse/category/sort contracts, shared `ActionPlans.ApplyScalar` and
`ReferenceInspection`, and SDK-05 `ReferenceViews.Icon` with `ReferenceIconOptions`.
Quantity application uses `ActionPlanWarningPolicy.Merge`;
eligibility, scope validation, unopened bindings and preview-only derived aliases
remain plugin policy. Fixed row actions encode their record key so
`ResolveSelection` enforces it on Core's fresh inspection document. Catalog-wide
pickers retain their action IDs and allow choosing other pinned records.

Remaining request: bounded F8 traversal in Core semantic review. Its original/current
view walk still does not query F8 collections. Quantity changes are listed by value
ID, but query-only inventory fields have no semantic-review jump or individual Revert;
use undo/redo or edit the loaded quantity field. F6 previews still include named
old/new quantities and apply as one undo group. No duplicate eager editor is added.

SDK-05 is resolved and adopted: `AriseAssets.Icon` delegates to the five-argument
`ReferenceViews.Icon` overload with the document's pinned context. `AssetIdOnly`
preserves image nodes for any non-null asset ID, including missing bytes/provenance;
`ExactAltText` retains the exact role text and publisher notice. Missing records or
asset IDs retain `TextNode("—")`. Characterization tests and catalog/sample view
fingerprints pin these nodes, detail attribution and inspection descriptions.
Map artwork, rich catalog filtering and reference detail prose also remain local.

The F6 reference-parameter fix needs no migration: setting quantities uses an integer
parameter, and restoring imported quantities has no parameters. Float-duration hints do not apply to the integer counters.
The shared request register is [`docs/SDK_REQUESTS.md`](../../docs/SDK_REQUESTS.md). Existing bindings,
reference packs, queryable tables, F6 planning/query resolution, lazy sections and
paged blobs support the current plugin. Evidence, rather than a missing framework
contract, gates equipment, arte/skill and progression writes.
