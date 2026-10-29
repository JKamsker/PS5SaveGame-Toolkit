# GodOfWarRagnarok — improvement plan

> **Historical planning snapshot — 2026-10-28.** The baseline, proposed work, test counts and
> policy restrictions below describe that research round, not current implementation or permissions.
> See [current plugin status](../PLUGINS.md), the linked plugin README,
> [delivered framework features](ROADMAP.md) and [current policy](../PLUGIN_GUIDE.md#hard-rules).

## Summary

The plugin is a careful, bounded ReadOnly explorer for PPSA08330: it validates the observed header, metadata text, directory arrays, hero-wallet span, resource occurrences and preferences while preserving every byte. A massively better editor would expose a named, searchable inventory and gear model, per-character XP/resources, progression and collectible views, map/quest status, safe bulk actions and eventually validated writes. The three blockers are the missing nested serialization grammar, unknown integrity/authentication coverage, and lack of controlled before/after saves plus console validation. PC and PS4 tools provide strong leads for IDs and roles, but their revisions and containers do not prove PS5 compatibility.

## Baseline state (historical evidence)

- [plugins/GodOfWarRagnarok/README.md](../../plugins/GodOfWarRagnarok/README.md) documents eight catalogued regional IDs, with samples only for PPSA08330; GOWRSAVE0–65 and userpreferences are recognised.
- GodOfWarRagnarokPlugin.cs probes title ID + filename + observed header and opens one file; CanEdit is false and maturity is ReadOnly.
- RagnarokFormat.cs validates little-endian kind 8/31, revision 0x00513040, 696-byte preferences, metadata offsets, directory marker 0x61C0, size/count bounds and UTF-8 description.
- RagnarokRecordIndex.cs verifies metadata kind/revision, strictly increasing 798-ID directory, 0/2 paired-word span, ten secondary IDs, exactly one hero-wallet marker 0x00004895C643225F, bounded 16-byte rows and the FFFFFFFF observed EOF sentinel.
- RagnarokDocument.cs publishes Overview, Wallet, partial Record index, Resource anchors, Raw and Preferences tabs; tables/raw bytes are lazy and bounded. It has no setters, actions, assets or structural edits.
- RagnarokTables.cs labels wallet quantities Inferred; duplicate resource occurrences remain separate. ResourceNames.json has 18 cited IDs (Hacksilver, Rawhide, flames, six dews and Valhalla items), no full gear/quest dictionary.
- Tests in SaveToolkit.Plugins.GodOfWarRagnarok.Tests cover malformed headers/counts/IDs/text, truncation, bounded expansion, byte-identical serialization, all supplied records and backup twins. They establish parser preservation, not an editable grammar or in-game acceptance.
- Scratch verification (read-only Python over extracted samples): GOWRSAVE65 is 3,214,711 bytes with kind 8, revision 0x00513040, metadata kind 35, 798 sorted IDs, ten secondary IDs, wallet at 0x1BC84 with 211 rows; wallet Hacksilver at 0x1BCC8 has quantity 288,643, Rawhide at 0x1BD18 has 52, Chaos Flame at 0x1C0F8 has 0, and the 2018 EconomyXP hash at 0x1BC98 has 84,725. GOWRSAVE0 has the same structural constants but an empty wallet. These are byte facts; role/meaning remains inferred.
- The SDK already supplies SectionNode/lazy expansion, TabsNode, editable FieldNode with IntegerSpec/EnumSpec/FlagsSpec, searchable virtualised TableNode, ActionNode, ImageNode, HexNode, confidence/warnings, atomic ApplyEdits, undo-compatible Changes, multi-file Serialize, GetAsset and blob paging. web/src/components/document/TableView.tsx searches/virtualises tables, while FieldControl.tsx has scalar/enum/flags controls only; there is no item-picker, icon grid, map, diff or reference-data loader.

## Research findings

### Existing editors/tools (same game, other platforms)

- [SaveWizard-Resources GOW Ragnarök](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt) (commit 4c32596efde54af7bec390b298265f09c929d94d, 2026-10-09, repository GPL-3.0) supplies literal resource hashes, item-replace/quantity code patterns, chest capacity notes, and a PS4 difficulty pointer at 0x2F442. The search IDs are byte patterns, not proof of a PS5 offset or checksum; the exact Hacksilver, Rawhide, Chaos Flame, Gale Flame, dew and Valhalla byte patterns match occurrences in the supplied PS5 samples. Treat as research facts only; do not port GPL code/data wholesale.
- [Apollo Save Wizard format](https://github.com/bucanero/apollo-lib/blob/cf1f1823a534f35d03fc41ef2c5b641e7fde30d4/docs/savewizard.rst) (Apollo commit cf1f1823a534f35d03fc41ef2c5b641e7fde30d4, 2026-10-26, GPL-3.0) explains search, pointer-relative writes, endianness and conditional/multi-write semantics. It can guide a differential-analysis harness, but it does not provide Ragnarök serialization or integrity.
- [Codes Prince PS4/PS5 editor listing](https://codesprince.com/shop/) confirms separate commercial Ragnarök console and PC products, but closed-source feature claims are not an algorithm or licence to reuse. [NinjaSaves PPSA08330](https://ninjasaves.com/cheats/god-of-war-ragnarok-ppsa08330) lists 64 beta options and checks exact saves after import; it is useful evidence that resources and character XP are practical targets, not evidence of safe local writing.
- [Apollo PS4 save database for CUSA34386](https://bucanero.github.io/apollo-saves/PS4/CUSA34386/) and [NextGenUpdate region-transfer report](https://nextgenupdate.com/forums/ps4-trophies-game-saves/1038017-ready-ng-god-war-ragnarok-cusa34384cusa34388a.html) show PS4 save sets/region workflows. They do not establish that PS4 files or offsets transfer to PPSA08330.
- [GoW Save Lab](https://github.com/xcier/GoW-2018-PS4-editor/tree/9342d5f3967ef74bac1161581dc4eab8bb41760d) (commit 9342d5f3967ef74bac1161581dc4eab8bb41760d, 2026-06-03, no SPDX licence) is a real decrypted PS4 memory.dat editor: 20 slots, fixed slot-relative inventory table, 16-byte ID/u32 quantity/metadata rows, XP/Hacksilver sync, and searchable item JSON. It is a strong data/UX lead, but the PS4 multi-slot container and fixed base+0x1041D layout are not present in the PS5 sample; do not port its code or assume offsets.
- [Continue? Ragnarök reader](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py) (commit 51e19b2a292c60f9acdf3c13c561809657104ea2, 2026-10-16, GPL-3.0) reads PC GOWRSAVE<N> files, identifies description 0x50..0x190, play seconds 0x190, objective ID 0x1B0, save counter 0x1BC, hero wallet rows at marker+20, and the PC revision 0x00660404. PS5 samples share the first offsets/marker but use revision 0x00513040; controlled PS5 evidence is required before any transfer. The source explicitly warns that level and some text interpretations are not proven.
- [Cheat Engine table](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT) (commit 7b1730217b92d27ab910d6d421efbd503cc109b2, 2026-08-02, no SPDX licence) targets PC runtime functions (resources_FindWallet, Wallet_ModifyResource, StdNameHash) and lists Kratos/Atreus/Freya XP and many resource hashes. It confirms useful name-hash/runtime leads, not on-disk save grammar or checksum.
- [Santa Monica save-location guidance for PC Ragnarök](https://support.sms.playstation.com/hc/en-us/articles/30374735162381-Find-save-data-files) and [SteamDB UFS manifest](https://steamdb.info/app/2322010/ufs/) establish PC Saved Games/God of War Ragnarök/<user-id>/GOWRSAVE* and userpreferences; official guidance warns tampering can lose progression.

### Same engine / same studio

- [GDC “The Future of Scene Description on God of War”](https://www.gdcvault.com/play/1025969) states Santa Monica uses a proprietary engine and a data-description system. [Shacknews' engine confirmation](https://www.shacknews.com/article/104537/what-engine-does-god-of-war-2018-run-on) independently says the 2018 game uses an in-house engine. This supports searching shared name-hash/data-description conventions, but no public sibling save writer has been found.
- [MorseTheCode/GodOfWarTool](https://github.com/MorseTheCode/GodOfWarTool/tree/d8fdda4993d593825b1a2206904ab85c86fd703a) (commit d8fdda4993d593825b1a2206904ab85c86fd703a, 2026-06-20, no SPDX licence) reads/rebuilds 2018 and Ragnarök WADs, including Ragnarök WTOC v2 + LZ4, and extracts MSGS_TXT/textures. This is game-asset evidence only: WAD headers are not GOWRSAVE headers and no save checksum is supplied.
- [iArtorias/smpack](https://github.com/iArtorias/smpack/tree/6d4d7091d36aa1a3df00170f685c9130b0de25a9) (commit 6d4d7091d36aa1a3df00170f685c9130b0de25a9, 2026-10-06, GPL-3.0) documents WTOC/LZ4 and user-owned PC asset extraction; it ships no game data. Use its format knowledge only, or isolate GPL tooling outside this plugin.
- [JeanxPereira/GoWToolkit](https://github.com/JeanxPereira/GoWToolkit/tree/94a6df0f2cde2ea60f13a5904fa44d3ce6234ee7) (commit 94a6df0f2cde2ea60f13a5904fa44d3ce6234ee7, 2026-10-16, repository licence text says educational/research; no SPDX) supports Ragnarök PS4/PS5/PC WAD, GNF and map viewers. It is a lead for extracting icons/maps from an entitled install, not a save parser or reusable dependency.
- No public same-studio/same-engine project found with a complete Ragnarök/2018 save serializer. All transfer confidence for nested records, structural edits and integrity is Low until PS5 differential data or a clean-room grammar is obtained.

### Game data sources (names, descriptions, icons, limits, quests)

- [SaveWizard resource table](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt) and [PC Cheat Engine hashes](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT) cover dozens of resource/XP IDs and categories, but have no complete descriptions, legal maxima or stable on-disk mapping. Cross-check IDs against wallet rows and retain Unknown entries.
- [GoW 2018 item JSON](https://raw.githubusercontent.com/xcier/GoW-2018-PS4-editor/9342d5f3967ef74bac1161581dc4eab8bb41760d/app/resources/database/gow2018_items.json) has IDs, names, type and internal resource filenames for 2018. It is a no-licence community database and is the wrong game/version for direct embedding; use only as a lead for hash/name discovery or ask the author for permission.
- [God of War Wiki resource page](https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29) supplies human descriptions/categories for resources; [Fandom licensing](https://www.fandom.com/licensing) says wiki text is generally CC BY-SA 3.0 with attribution/share-alike, while media files have separate licences. Small factual descriptions can be regenerated with attribution; do not bundle Fandom/game icons without checking each file licence.
- [PowerPyx collectible guide](https://www.powerpyx.com/god-of-war-ragnarok-collectibles-guide-100-completion/) enumerates 418 tracked locations (47 favors, 38 artifacts, 80 lore, 48 ravens, chests, rifts, tears, flowers, Lindwyrms, stags, trials, maps and relics); [walkthrough](https://www.powerpyx.com/god-of-war-ragnarok-walkthrough/) lists 18 main missions + 47 favors. [Push Square's guide](https://www.pushsquare.com/guides/god-of-war-ragnarok-guide-walkthrough-all-collectibles-and-wiki) provides gear categories and locations. These are HTML editorial sources with unknown redistribution terms: use links/hand-curated factual IDs, not copied prose.
- A user-owned PC install plus [smpack](https://github.com/iArtorias/smpack/tree/6d4d7091d36aa1a3df00170f685c9130b0de25a9) or [GoWTool](https://github.com/MorseTheCode/GodOfWarTool/tree/d8fdda4993d593825b1a2206904ab85c86fd703a) can supply r_lang_* / MSGS_TXT, item/quest strings and WAD/TEXPACK icons. The repository must not redistribute Sony assets; add a user-supplied reference-data import/cache in Core, then embed only generated factual maps or let users supply assets locally. The save's 64-bit IDs have not yet been proven to equal every asset hash.
- No source currently supplies verified per-item maximum stack/legal range for PS5. Start with observed u32 storage and conservative, human-approved ranges; never claim a max from a trainer's 2147400000 preset.

### Integrity / crypto

- The local [PPSA08330 format reconnaissance](../../docs/formats/PPSA08330.md) and plugin RESEARCH.md report finite tests of CRC32/CRC32C-not-yet-tested, Adler32, xxHash32/64 and Murmur candidates over header/footer, directory and wallet spans with no match. This is a negative result, not proof of no integrity field.
- Scratch bytes show a stable 0xFFFFFFFF final u32 and stable structural words, but the sentinel does not behave as a checksum. The PC reader's revision 0x00660404 differs from PS5 0x00513040; PS4 difficulty 0x2F442 is unsafe to transfer (the existing corpus has values outside the published enum).
- No key, signature scheme, compression transform or whole-record length grammar is established. Existing samples change many opaque bytes between snapshots and identical backup twins yield no checksum equation. Commercial editors and trainers expose outcomes, not their implementation.
- Before writing, obtain controlled pairs (one field changed per save, same title/build/slot), hash/diff every file, identify all derived bytes, and test no-op/edit/reopen plus console load. If a PS5 save rejects a locally edited file, keep the plugin ReadOnly and report the exact dependent bytes rather than guessing.

## Vision

- Overview: slot/file identity, realm/area/objective split from the verified description, play time/counter with confidence, revision/build warning and integrity status.
- Characters: Kratos, Atreus and companion/guest sections with XP, level/equipment score, health/stats and skill unlocks only when IDs and dependencies are proven; raw fallback for every unmapped field.
- Inventory: lazy searchable table of wallet/container, hash, sourced name, category, description, icon, quantity, metadata and location/ownership; unknown rows remain editable only as raw values after validation.
- Gear: equipped slots and owned weapons/armor/attachments/runes, upgrade level and derived stats, with presets that preserve dependencies.
- Progress: quests/favors, realm/map unlocks, collectibles and Valhalla state as checkboxes/tables with source links and confidence.
- Actions: set resource, max safe existing stacks, apply a named preset, and compare slots; all are bounded, confirmable and undoable, with structural add/remove deferred until grammar supports it.
- Raw/Diagnostics: lazy byte pages, parsed record spans, unresolved hashes, diff report and integrity diagnostics; never hide bytes the plugin does not understand.

## Phased plan

### Phase 1 — evidence and grammar (goal: make the existing read model complete and falsifiable)

- [ ] Capture PS5 controlled pairs for one existing wallet resource, each XP wallet, one gear upgrade, one quest flag and one collectible; record game version, title ID, slot and UI before/after.
- [ ] Map all metadata strings/IDs and locate nested record boundaries using count/length invariants; preserve unknown regions as slices.
- [ ] Compare PC/PS4/hash leads against every regional/revision sample; reject layouts that rely on fixed offsets or first-match searches.
- [ ] Add differential fixtures, byte-range annotations and corruption tests outside the repository samples.
- Evidence: at least three independent saves per field and one late/Valhalla save. Risk: high; rough size L.

### Phase 2 — reference data and read-only product views (goal: useful names without unsafe writes)

- [ ] Build a versioned factual hash dictionary from cited community IDs and user-supplied PC strings; record source, build, locale and confidence per entry.
- [ ] Add resource/category/description tables and raw-ID fallback; do not copy unlicensed prose or game media.
- [ ] Parse character wallets, gear and progression only where ownership and boundaries are proven; expose unresolved records in Diagnostics.
- [ ] Add icon/asset IDs that resolve through a user-provided asset cache, with no plugin I/O/network.
- Evidence: each displayed name maps to a sample ID and source; no-op serialization remains byte-identical. Risk: medium; rough size L.

### Phase 3 — safe scalar editing (goal: validated existing-value writes)

- [ ] Implement in-place setters for existing wallet quantities/time only after integrity coverage is known.
- [ ] Publish narrow specs from measured game limits; reject overflow, impossible metadata and missing dependencies.
- [ ] Recompute and self-verify every checksum/signature/compressed block; reopen serialized bytes before returning.
- [ ] Add undo changes, max safe existing stacks action and export review warnings; keep maturity Experimental until console load succeeds.
- Evidence: edit→serialize→reopen fixtures for all supported revisions plus a real PS5 load. Risk: very high; rough size L.

### Phase 4 — structural inventory/gear/progression (goal: real editor)

- [ ] Add row add/remove/replace only after free-slot, ownership and length rules are proven.
- [ ] Add cross-field references (equipped item ↔ inventory, XP ↔ level, quest prerequisites), atomic multi-file edits and dependency-aware presets.
- [ ] Add collectible/map/quest bulk actions and slot diff/merge with conflict review.
- Evidence: controlled saves prove each dependency and recovery from rejected edits. Risk: very high; rough size XL.

## Requirements

| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
|---|---|---|---|---|---|
| GOWR-R1 | SDK | Stable typed references between fields/rows and a bounded add/remove/replace-row operation with atomic StructureChanged results. | Gear/inventory/progression dependencies and Phase 4 structural edits. | Yes for structural writes | Keep existing rows and expose raw tables; plugin actions can remain read-only. |
| GOWR-R2 | SDK | Reference-data/asset identity metadata on FieldNode/ImageNode (source/build/licence and fallback text). | Names/icons need provenance and user-supplied assets in Phase 2. | No for raw views; yes for rich views | Embed the current 18-name dictionary and show IDs/links. |
| GOWR-R3 | Core | Import/cache API for user-provided PC WAD/string/icon data with no network or plugin filesystem access. | Converts entitled game data into a versioned dictionary/cache. | Yes for broad names/icons | Hand-maintained embedded factual IDs; no icons. |
| GOWR-R4 | Core | Differential-save fixture store and byte-range/digest diagnostics; multi-file atomic export with preflight backup. | Required to discover and safely recompute integrity in Phases 1/3. | Yes for writing | Manual external scripts and read-only Serialize(). |
| GOWR-R5 | UI | Item picker/search with icon, category, description, source/confidence and legal-range warning. | Inventory/gear usability in Phase 2/3. | No for scalar prototype | Searchable virtualised table with hash/name text. |
| GOWR-R6 | UI | Tabs/widgets for character sheets, gear grids, quest/collectible checklists, maps and slot diff/merge. | Vision and Phase 4 workflows. | No for current explorer | Lazy tables and raw diagnostics. |
| GOWR-R7 | Data | Versioned hash→name/category/description/limit dictionary covering PS5 IDs, PC build and locale; document provenance. | Prevents invented names and enables validation. | Yes for named editing | Show Unknown ID 0x…; current 18 IDs only. |
| GOWR-R8 | Data | Licence route for text and media: CC-BY-SA attribution/share-alike for permitted wiki text, per-file permission or user-supplied cache for icons, no Sony assets in repo. | Legal distribution of Phase 2 references/assets. | Yes for bundled icons/prose | Links and user-provided assets only. |
| GOWR-R9 | Tooling | Clean-room parser/diff harness for counts, nested lengths, hash candidates, compression and checksum coverage; fixtures never committed from artifacts/. | Grammar/integrity discovery in Phase 1. | Yes | Existing bounded parser and finite negative hash probes. |
| GOWR-R10 | Tooling | Property tests for no-op byte identity, corruption rejection, range limits, cross-revision probing and serialize→reopen. | Required before enabling any setter. | Yes | Current read-only tests. |
| GOWR-R11 | Samples | Same slot before/after one Hacksilver/resource/XP/gear/quest/collectible change, including PS5 and Valhalla revisions and preferences. | Only credible route to semantics, offsets and derived bytes. | Yes | Keep all edits disabled. |
| GOWR-R12 | Samples | PS4 and PC saves paired with the same gameplay state, plus at least two regional PS5 IDs. | Calibrates transfer confidence and revision-specific grammar. | No for PPSA08330 read-only; yes for portability | Support PPSA08330 only. |
| GOWR-R13 | Human | Owner supplies legally obtained PC asset dump/cache and approves licence/attribution policy. | Needed for names, descriptions and icons without redistribution risk. | Yes for rich assets | Factual names from cited public IDs. |
| GOWR-R14 | Human | Human performs restore/load tests on spare PS5 slots and records game version, rejection text and in-game values. | Determines Stable status and legal ranges. | Yes for writes | ReadOnly/Experimental labels and backups. |
| GOWR-R15 | Human | Decide whether unsupported structural edits (new item rows, quest completion, map unlock) are out of scope until a proven writer exists. | Prevents unsafe max all promises in Phase 4. | Yes for product scope | Existing-value edits only, if integrity is solved. |

## Acceptance criteria

- [ ] Keep all implementation inside plugins/GodOfWarRagnarok/ and document every source, revision, confidence label and licence decision.
- [ ] Add no setter until nested boundaries and integrity coverage are demonstrated for the target PS5 revision; preserve unknown bytes and byte-identical no-op output.
- [ ] Every named item/quest/collectible shown has a sourced ID mapping verified against a sample or is explicitly Unknown; no copied unlicensed game art/text is bundled.
- [ ] Read-only views remain bounded/lazy and open all supported regional/revision samples without parser exceptions.
- [ ] Differential fixtures prove each supported scalar edit, derived-field change and corruption rejection; Serialize() reopens its own output.
- [ ] Console validation on a backed-up spare slot confirms resource, XP, gear and progression edits before maturity advances from ReadOnly to Experimental/Stable.
- [ ] Bulk actions are bounded, confirmable, atomic and undoable; structural actions report dependencies and refuse unsafe rows.
- [ ] Preferences stay separate until one-setting-at-a-time captures establish their meanings and integrity.

## Open questions & risks

- Is 0x00513040 a revision only, or does a hidden per-build grammar/integrity variant exist? PC 0x00660404 cannot be assumed compatible.
- Where do nested record lengths, ownership links, equipped-item references, quest flags and map bitsets live beyond the indexed arrays? The current 798-ID directory has no established semantic names.
- Are the repeated resource occurrences wallet rows, chest templates, totals or runtime caches? Never sum them without a proven role.
- Does PS5 use a keyed hash/signature, encrypted sub-block, compression or external metadata not represented by tested candidate hashes? A finite no-match search cannot answer this.
- Can a user-provided PC asset dump’s hash scheme map all save IDs, including DLC/Valhalla and regional/localised strings? Expect gaps and build drift.
- What are legal stack limits and XP/level side effects? Trainer values are not game limits; a bad value may soft-lock progression.
- Preferences are exactly 696 bytes but differ at 29 positions; meanings, ranges and integrity are unknown.
- Console testing is destructive if backups/restore are mishandled; keep the plugin ReadOnly and preserve original archives until a human validates every edit.

