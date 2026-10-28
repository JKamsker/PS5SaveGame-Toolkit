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
- Curated gald, six character levels/current HP/SP, existing recognised consumable
  quantities, and read-only play time, experience and base HP. All gameplay meanings
  carry **Inferred** confidence for the PS5 mapping.
- Party stats and character table, lazy inventory table, lazy read-only explorer of
  all 30 sections / 138,832 primitive uint32 words, and paged plaintext/outer hex views.
  Primitive pages contain at most 256 rows. Opening and building the initial view
  stays below two seconds in the sample tests.
- Serializes fixed-size patches, re-encrypts in chain order, recomputes SHA1, then
  decrypts and validates the output and compares every plaintext byte after the header.
  Even no-op serialization runs the actual cipher and is byte-identical on all six files.
- Preserves the entire outer region `0..0x32057`, including screenshot/header bytes.
  Unknown fields, record flags, IDs, padding and unvisited bytes remain unchanged in plaintext.
- Ships the `talesofarise.catalog` F2 pack for `PPSA02745` (English, **v2.0.0**):
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
  blocks expose raw bytes only. Read-only aliases stay current after supported edits.
- Arte/skill views show published requirements, costs and effects for 309 character-
  specific artes, 64 skill definitions, 108 title panels and 540 individual nodes.
  Usage, proficiency, title unlock, purchase and panel-completion joins are Unknown.
- Progress views combine 128 recipe/fish/artifact guide records with separate raw
  section evidence for owls, quests/scenario, fishing, camps/maps, PartyProfile and
  ItemManager. They do not infer ownership, learned/cooked, caught/rewarded, found/
  reported/rewarded, effect toggles or completion percentages. The only bulk action is
  the bounded existing-row quantity action, which uses F6 planning and grouped undo.
- F9 adds six bounded skill-panel graphs and six arte-prerequisite graphs. It also adds
  an original schematic progress map with attributed external links for 128 guide rows
  plus owl, camp and map domains. Coordinates are plugin-authored grouping positions,
  never claimed in-game coordinates; every saved state remains Unknown.
- Inventory is also exposed through an F8 queryable collection. Core can resolve a
  filtered inventory query into stable fixed-slot row keys for the F6 quantity preview;
  query pages keep quantity cells read-only so only the expanded editor owns writable
  bindings.
- Records section-relative evidence in `Resources/EvidenceLedger.csv`. `AriseDiff` aligns
  decrypted bodies by schema ID and reports relative changed offsets, so shuffled physical
  addresses do not become false field evidence. No new gameplay meaning is promoted by this ledger.

`SystemSaveData.sav` is a separate **4,096-byte opaque system blob**. It is not claimed,
decrypted or rewritten; the framework's raw viewer remains available. Each document
only returns its own file, without silently changing the console backup twin.

## Framework feature adoption

| Feature | Status | Concrete adoption or reason |
| --- | --- | --- |
| F1 SaveDiff | Used | `AriseDiff` aligns decrypted sections by schema ID and relative offsets; twin and slot tests prove the alignment. The shared `PluginViewDecoder` can traverse the plugin without plugin I/O; private sample bytes stay outside git. |
| F2 reference packs | Used | The embedded `talesofarise.catalog` v2 pack is opened through `IReferenceContext`; dataset and per-record attribution appear in catalog descriptions and the Sources panel. |
| F3 rich table cells | Added this round | Catalog and inventory rows expose read-only inspect actions, detail/provenance text and confidence badges. No image cells are included because no icon licence is cleared. |
| F4 reference pickers | Used | Inventory ID fields and all catalog identity columns use `ReferenceSpec`, preserve unknown numeric IDs and allow custom fallback. |
| F5 structural edits | N/A | No safe add/remove/reorder writer exists: allocation, inverse records and acquisition invariants are unproven, so fixed-slot scalar writes remain the only structural-safe scope. |
| F6 planned actions | Used | `inventory/set-quantity` previews changes, skips unsupported rows, revalidates at apply and records one undo group. Core-resolved F8 query scopes are accepted and tested. |
| F7 document groups | N/A | `SystemSaveData.sav` is opaque and the console backup twin has no proven independent ownership contract; this plugin intentionally edits one selected gameplay file only. |
| F8 queryable collections | Used | Catalogs page and sort all 2,157 records; the inventory collection supplies stable fixed-slot rows for search and query-scoped planning. |
| F9 maps and graphs | Added this round | Skill-panel and arte prerequisite DAGs plus an external-link-first schematic map use bounded `GraphNode`/`MapNode` views, text alternatives, stable targets and spatial validation. |
| F10 exact numbers / nullable unions | N/A | The supported PS5 schema is fixed-width integers and bytes; it contains no JSON numeric lexemes or nullable/union fields requiring F10. |
| F11 shared codecs | N/A | Tales of Arise uses its measured AES/XOR/SHA1 format; it has no Kraken or SQLite payload, so `SaveToolkit.Codecs` does not apply. |
| F12 SaveDiff F8 traversal and decoder hooks | Pending #32 | The framework feature is not on `main` in this round; no plugin-local substitute is added. |
| F13 inspect/picker/filter and float-duration improvements | Pending #33 | The framework feature is not on `main` in this round; current read-only actions and integer duration remain on existing contracts. |

## Enabled writes and their evidence

All rows below remain **Experimental** and retain visible warnings. The PS5 field
meanings are **Inferred** unless stated otherwise; the owner must complete the linked
[owner validation tracking issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
before promoting any feature or plugin to Stable.

| write | confidence | evidence | integrity status | owner validation needed |
| --- | --- | --- | --- | --- |
| PartyProfile gald (`party/gald`) | Inferred | Body `+0x18` matches the cited PS4 mapping; fixed-width samples and atomic edit/reopen tests. All supplied samples are zero, so no in-game value change is proven. | Known ciphertext SHA1 is recomputed; AES/XOR re-encryption reparses and compares plaintext. | Buy and consume one item, then save/reload and test normal acquisition; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| Existing active-character level, current HP and SP (six fixed status blocks) | Inferred | Body offsets match the cited PS4/PC layout and sample values; edit/reopen and unrelated-byte preservation tests. Level dependencies and effective stats are intentionally untouched. | Known ciphertext SHA1 is recomputed and self-checked; load-screen preview and dependent caches are preserved. | Load a spare edited slot, verify menus, perform a normal level/HP/SP event, save/reload; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| Existing recognised consumable quantity (IDs 1–13, 0–15) | Inferred | 13 legacy numeric mappings; Apple Gel is observed once in slot 002. Fixed record offsets, flags/IDs/order preservation, range rejection and edit/reopen tests. | Known ciphertext SHA1 is recomputed and self-checked; no records, IDs, flags or padding are changed. | Buy/use an item, test zero and later acquisition, and validate normal/effective stack caps; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |
| `inventory/set-quantity` F6 action over selected or document-scoped existing recognised rows | Inferred | Deterministic preview, skipped unsupported rows, bounded target 0–15, apply-time revalidation and one grouped undo. No record creation or grant behavior. | Same known SHA1/AES self-check as scalar edits; the game may reject inferred semantics. | Restore a spare slot, apply a preview, verify the effect and later acquisition after reload; [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). |

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
| SaveData_ItemManager | `i * 16` | Stack quantity byte | Editable only for existing IDs 1–13, 0–15; F6 can set selected/document-scoped recognised rows |
| same | `i * 16 + 1` | Flags byte | Raw, preserved |
| same | `i * 16 + 2` | Item ID, uint16 | Read-only; 934-record scan from cited PS4 patch |

Character names and the 13 consumable IDs are embedded reference tables; unknown
item IDs retain numeric labels. Empty record IDs are omitted; there is no item
insertion, flag editing, accessory editing or bulk grant action. Existing values
outside the supported editing guardrails remain visible but read-only. IDs 6/7 of the
character sections are unmapped and read-only, not assumed to be active party members.

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

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests
python -I -X utf8 plugins/TalesOfArise/ReferenceData/test_wiki_tables.py
```

Round-2 plugin suite: **59 passed, 0 failed, 0 skipped**, including the inherited
conformance suite and a Core F8 session integration test. Python parser suite:
**3 passed**. API build: **zero warnings/errors**. Earlier standalone Core-suite
counts are historical; this round does not claim to have rerun that suite.
Tests cover independent cipher vectors with short feedback updates and key evolution,
SHA1 `abc`, independently calculated whole-plaintext SHA256 digests, all six real
gameplay records, known values, no-op, edits/reopen/undo, exact preservation of every
other plaintext byte and outer bytes, atomic rejection, corruption, valid-SHA1
malformed encrypted structures, unsupported modes/schema, and every explorer page.
Sample tests use `SampleSaves` and skip when the archive is unavailable.
An explicit missing-archive run passed 9 independent tests and skipped 30 sample tests.
The embedded catalog contains attributed names, reference facts and adapted descriptions;
no sample data or downloaded reference implementation is embedded. New tests cover all
catalog pages, pinned licence attribution, title-node relations, source duplicates and
unknowns, tail searches, character/title filters, deterministic sorting, bound read-only
query fields, live aliases, and byte-identical read-only views on all six gameplay files.

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
are the SDK and .NET cryptography only, with no added NuGet packages.

- [ItsAllAboutTheCode/save-converters notes](https://github.com/ItsAllAboutTheCode/save-converters/blob/main/save_convert/tales_of/arise/save_NOTES.md):
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
- [Sora3100 Arise.CT](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/master/Arise.CT):
  character index/name ordering and PC level/HP/experience/SP/base-HP layout.
  PC runtime addresses are not used as save-file addresses.
- [Gald Editor author page](https://www.nexusmods.com/talesofarise/mods/407):
  consulted for existing tools; this is a PC memory table, not PS5 save-layout evidence.
- [Local reconnaissance](../../docs/formats/PPSA02745.md) and
  [method](../../docs/formats/README.md): initial archive measurements and offset discrepancy.
The following derived data is by **Aselia Wiki contributors**, retrieved **2026-10-28**,
and remains **[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)**.
Names and published facts/descriptions were extracted, whitespace normalized, images
and navigation omitted, and symbolic keys added. Unknowns/source duplicates were
retained. Every record has its own revision URL; every source is also in `Info.References`
and UI descriptions/provenance. Dataset attribution appears in the Sources panel.

| Dataset | Coverage | Pinned source / attribution |
| --- | --- | --- |
| `items` (271) | All named consumables, materials, accessory materials, valuables, fishing items and artifacts | [Consumables r382800](https://aselia.fandom.com/wiki/ToA_-_Consumables?oldid=382800); all six lists in the [source ledger](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/data.source.md) |
| `equipment` (675) | Six weapon/armor types, accessories, costumes, styles and attachments | [Swords r363915](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Swords)?oldid=363915); every category/character revision in the source ledger |
| `artes` (309) | Six playable and thirteen allied/enemy/cameo character pages; character-specific Boost Strikes | [Alphen artes r378115](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Artes?oldid=378115); all 19 pages and Boost Strike cross-check in the source ledger |
| `skills` (64) | All parameter/action/character skills, published base/DLC stacks, effects and unknowns | [Skills r378099](https://aselia.fandom.com/wiki/ToA_-_Skills?oldid=378099) |
| `titles` (108) | All six characters' 18 panels, requirements and bonuses | [Alphen panels r378024](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Skill_Panels?oldid=378024); six character revisions in the source ledger |
| `skill-nodes` (540) | All five positions per panel, SP text and title prerequisites | Same six pinned panel pages; dash costs remain distinct from explicit zero |
| `characters` (62) | Every named character/group in the navigation template; six inferred playable status joins | [Character template r300086](https://aselia.fandom.com/wiki/Template:ToA_Characters?oldid=300086) |
| `progress` (128) | 45 recipes, 44 fish species, 39 artifacts; guide data, no saved state joins | [Cooking r379806](https://aselia.fandom.com/wiki/ToA_-_Cooking?oldid=379806), [Fishing r350354](https://aselia.fandom.com/wiki/ToA_-_Fishing?oldid=350354), [Artifacts r381166](https://aselia.fandom.com/wiki/ToA_-_Artifacts?oldid=381166) |
| `Characters.csv` | Six short names plus unmapped placeholders; existing index mapping remains Inferred | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Characters.source.md) |
| `Consumables.csv` | 13 legacy inferred numeric mappings; no newly established IDs | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Consumables.source.md) |
| `Sections.csv` | Published section labels plus independently measured schema facts; GPL-3.0 format research, no code port | [Adjacent attribution](SaveToolkit.Plugins.TalesOfArise/Resources/Sections.source.md) |
| F9 schematic coordinates and external map links | Original plugin-authored grouping positions; no game art or coordinates copied | [Repository licence](https://github.com/JKamsker/PS5SaveGame-Toolkit), [Tales of Arise guide index](https://aselia.fandom.com/wiki/Tales_of_Arise), [Owls](https://aselia.fandom.com/wiki/ToA_-_Owls), [Camps](https://aselia.fandom.com/wiki/ToA_-_Camps); map art remains external-only and Aselia text remains CC BY-SA 3.0 |

The [data sidecar](SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/data.source.md)
lists all **62 immutable revision URLs**, author/licence/date/modifications and coverage
limits. `source-revisions.json` pins rendered response and wikitext SHA-256s; the pack
manifest pins all record files. [Maintenance scripts](ReferenceData/README.md) reproduce
the extraction from hash-checked downloads held outside the repository. No unlicensed
datamine, copyrighted icon bundle or downloaded implementation was copied this round.
Wiki references include DLC/expansion facts; the supported save schema remains base-game
PPSA02745 only. No complete licensed owl/quest identity table or save join was established.

## SDK requests

None. The existing bindings, queryable tables, reference picker, lazy sections and paged blobs
support this plugin. Evidence, rather than a missing framework contract, gates equipment,
arte/skill and progression writes. F6 planning and F8 query-scope resolution are available
when the owner captures establish safe bulk operations; no plugin-local substitute was added.
