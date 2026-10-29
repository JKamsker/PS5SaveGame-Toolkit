# BaldursGate3 — improvement plan

> **Status: superseded.** Framework foundations and supported plugin subsets have shipped;
> the old phases below are not an active checklist or a claim that every proposed writer is done.
> Remaining evidence/console work is in [the owner queue](../OWNER_CHECKLIST.md), and missing
> framework contracts are in [SDK requests](../SDK_REQUESTS.md).
>
> **Historical planning snapshot — 2026-10-28.** The baseline, proposed work, test counts and
> policy restrictions below describe that research round, not current implementation or permissions.
> See [current plugin status](../PLUGINS.md), the linked plugin README,
> [delivered framework features](ROADMAP.md) and [current policy](../PLUGIN_GUIDE.md#hard-rules).

## Summary

The `bg3` plugin is an Experimental LSPK/LSF resource editor with read-only party metadata, not yet a live character or inventory editor. A substantially better plugin would show named items with descriptions and icons, trustworthy character sheets, inventories, equipment, camp, spells and journal progress, then offer narrowly validated edits and bulk operations. Preserve the existing container writers: most missing work is semantic LSMF/Osiris decoding and evidence. The top three blockers are controlled PS5 differential saves, version-matched reference data with a usable licensing/import route, and successful PS5 load/resave validation of gameplay edits. This extends [issue #1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1) with stack-group details, ambiguous item resolution, icon extraction, multiplayer identity, dependency-aware edits and concrete framework requirements.

## Baseline state (historical evidence)

- Inspected 2026-10-28, read-only. Read `AGENTS.md`, `docs/PLUGIN_GUIDE.md`, `docs/PLUGINS.md`, `docs/SAMPLES.md`, `docs/formats/README.md` and the complete issue #1. `docs/formats/PPSA18463.md` does not exist; the formats index excludes BG3 from that reconnaissance. The plugin README and `INTEGRITY.md` are its existing format records.
- `plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3/BaldursGate3Plugin.cs` claims PPSA18463, version 0.2.0, Experimental. `BaldursGate3Document.cs` provides Overview/Party/Resources/Raw and records primitive replacements by resource/attribute ID. No live LSMF decoder exists.
- `PartyView.cs` reads `SaveInfo.json / Active Party / Characters`: origin, level and XP are Inferred, read-only metadata. `LeaderName` edits `meta.lsf` only, not a live character name. Two metadata character nodes versus three JSON rows prohibit positional one-to-one joins.
- `LspkPackage.cs`, `LspkLayout.cs`, `LspkIntegrity.cs`, `LspkWriter.cs`: supported single-part independent-entry v18 validation/writing, compression, MD5+1 repair and preservation. Legacy reads do not imply writable older formats; solid/multipart/deletion forms are rejected.
- `LsfReader.cs`, `LsfTables.cs`, `LsfBinary.cs`, `LsfWriter.cs`, `AttributeEncoder.cs`: LSOF v5–7 primitive writes and preservation of unknown bytes. GUIDs, translated strings, ScratchBuffers, structural edits, JSON/XML and story remain read-only. Gameplay writing needs an internal validated ScratchBuffer replacement path, not an arbitrary editable-blob SDK feature.
- `ResourceExplorer.cs` is lazy/paged; `BaldursGate3Document.GetAsset` exposes the package WebP. No item-name/icon/localization resolver, inventory ownership, stack, gold, camp, approval, journal or live spell control exists.
- Tests in `SaveToolkit.Plugins.BaldursGate3.Tests/{SampleFormatTests,SampleWritingTests,PartyViewTests,BaldursGate3ConformanceTests,LsfWriterTests,PackageWritingTests,ParserBoundaryTests}.cs` exercise sample structure, primitives, metadata, exact no-op, corruption and preservation. README/issue report 120 plugin tests and 43 framework tests passing previously. **No builds/tests were run in this research**, and those historical results do not establish gameplay compatibility.
- Independent verification reused scratch material and read the archive using an inspected, independently written Python script: `python -I <ps5plans>/scratch/BaldursGate3/independent_probe.py`. It decodes the file list, zstd entries and bounded LZ4 block/frames, including dependent blocks. No downloaded implementation/executable was executed. Primary/backup `.lsv` copies are identical: one independent snapshot.
- Verified package: 8,073,350 bytes, LSPK v18, seven entries; expanded `Globals.lsf` 2,410,069 bytes and `StorySave.bin` 33,628,992 bytes. Physical expanded-payload MD5+1 matches the header. Globals has 149,174 nodes / 189,357 attributes; its typed NewAge ScratchBuffer is 2,618,936 bytes, prefix `4c534d4601010008`. Extract the typed attribute, rather than scanning compressed data for LSMF.
- Verified directory convention: names begin at `u64(blob+16)+48 = 2,592,720`; there are **315** descriptors, not a fixed 355. All names and nominal array ranges fit, including arena-offset +48. Observed `(element bytes, nominal rows)`: EntityId `(16,9453)`, Classes `(16,256)`, Stats v3 `(36,256)`, Health `(32,380)`, SpellBook v3 `(16,291)`, ContainerSlotData `(16,638)`, IsOwned v1 `(8,554)`, NewStack `(8,38)`, StackEntry `(8,39)`. This verifies structure/presence, not ownership or gameplay interpretation.
- Verified 761 Item nodes and 761 string Stats attributes, 269 distinct Stats strings including exact `OBJ_GoldCoin` / `OBJ_GoldPile`; these are not established player-owned gold stacks. Also one Journal, two QuestsProgress, 40 RegisteredWaypoint and 256 Character nodes. Registered entries do not imply unlocked waypoints; Character nodes are not all party members. JSON has three level-1 rows with XP `(30,30)`, `(30,30)`, `(0,0)`.

## Research findings

### Existing editors/tools (same game, other platforms)

- **DanFinchUK/bg3-savefile-parser**, pinned [`9578ff7c46a1aa40c805f6b7beecf907f10fb3b8`](https://github.com/DanFinchUK/bg3-savefile-parser/tree/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8), strongest PC reader lead: [FORMAT](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md), [LIMITS](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/LIMITS.md), [LSMF reader](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/bg3parser/lsmf.py). Python/TypeScript implementations cover ECS, inventory, spells and story. No recognized licence in cached metadata/tree: obtain permission/clear licensing before adapting code, or implement observed format facts independently. PS5 transfer: **high for tested directory, unverified for semantic joins**. Our independent measurements corroborate component families/directory, not complete read coverage.
- **Critical refinements from the LSMF reader:** NewStack pointers lead to groups; StackEntry indexes the group's member array, not a global entity table. Several entries may contribute to one item's quantity. Current container membership differs from historical ownership. Packed Stats/Health and shifted ownerlists defeat simple descriptor-row offsets. These are read-only research leads, not verified PS5 editing addresses. Code licence unresolved; no port proposed until resolved.
- **LIMITS caveats:** shared Stats names, stale equipment flags, virtual slots and identical class builds require ambiguity handling. Matching position/class or selecting the largest spell book is not a general identity proof. Upstream reports transient stack state immediately after rearrangement; an absent settled group must not silently become an editable quantity of one. Concepts transfer with medium confidence, specific PS5 edge cases remain unverified.
- **Wahkah/BG3SE**, [`fb4b9d15068b3f48a630c2d68922c3a732f32699`](https://github.com/Wahkah/BG3SE/tree/fb4b9d15068b3f48a630c2d68922c3a732f32699), PC Python library/editor. [README](https://github.com/Wahkah/BG3SE/blob/fb4b9d15068b3f48a630c2d68922c3a732f32699/README.md) reports extensive reads but character-edit load failures. Its experiment results are upstream reports, not our reproduction. No recognized licence found: reference only until clarified. Medium structural transfer; no proven PS5 writer. Sample corroborates component names/sizes only. Later packed-stream research conflicts with its simpler Stats-row assumptions.
- **Hawk-on/bg3-save-editor**, MIT, [`6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15`](https://github.com/Hawk-on/bg3-save-editor/tree/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15). [FIELD_MAPPING](https://github.com/Hawk-on/bg3-save-editor/blob/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15/FIELD_MAPPING.md) separates template loot amounts from runtime gold. Inspected [SaveService.cs](https://github.com/Hawk-on/bg3-save-editor/blob/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15/backend/BG3.SaveEditor.Core/Services/SaveService.cs) labels large stacks as potential gold and quantities over 100 with a gold name. Reject that heuristic and its application cap as game rules. MIT permits attributed reuse, but this algorithm should not be adopted. Our sample confirms gold Stats strings and NewAge, not a writable quantity/owner mapping.
- **Serofix-code/bg3-save-editor**, [`e62134311f1ec737472448e4bfc9dda72afd4f3e`](https://github.com/Serofix-code/bg3-save-editor/tree/e62134311f1ec737472448e4bfc9dda72afd4f3e): PC local Node/Divine metadata/extraction workflow. [README](https://github.com/Serofix-code/bg3-save-editor/blob/e62134311f1ec737472448e4bfc9dda72afd4f3e/README.md) distinguishes Script Extender commands from offline edits. No recognized licence found; no code port. Medium metadata transfer, low gameplay-write confidence; not run/sample-validated.
- **clemarescx/bg3d**, MIT, [`09feb55fd5d7e601c57676ce1d161d56817d3ff5`](https://github.com/clemarescx/bg3d/tree/09feb55fd5d7e601c57676ce1d161d56817d3ff5): PC/browser inspection precedent. MIT permits attributed reuse; existing plugin already handles the relevant outer formats. No evidence of working PS5 ECS edits; no execution/semantic verification here.
- **Cheat Engine/Nexus:** [themaoci table, `6ea6b82b57d7d2567390447705f82fb8c24f7c25`](https://github.com/themaoci/BaldursGate3-CheatTable/tree/6ea6b82b57d7d2567390447705f82fb8c24f7c25) is GPL-3.0: behavioral/ID research, not a permissive plugin-code source. [Zanzer's author thread](https://fearlessrevolution.com/viewtopic.php?t=13996) and [Serofix's Nexus table](https://www.nexusmods.com/baldursgate3/mods/6917) concern PC runtime commands; Nexus notes underlying permission concerns. Low direct PS5/offline transfer; no execution/sample verification. A human can generate controlled PC observations, but plugin operation cannot require CE/SE.
- **Console search outcome, 2026-10-28:** searched GitHub/GitLab/Nexus, Apollo/PSX-Place/forum and template leads without finding an independently substantiated BG3 PS5 gameplay writer, Xbox-specific parser or BG3 010 template. This does not prove absence. [Save Wizard's FAQ](https://www.savewizard.net/faqs/) excludes native PS5 games. [Official Patch 8 announcement](https://baldursgate3.game/news/the-final-patch-new-subclasses-photo-mode-and-cross-play_138) identifies PC/Mac/Xbox/PS5 progression; no PS4/Switch BG3 save layout was established as a transfer target. Cross-save suggests shared families, not identical schemas or edited-save compatibility. No cloud/account wrapper was inspected.

### Same engine / same studio

- **Norbyte/LSLib**, MIT, [`6f5f6987d10d8485876b8368431c948e4dea4f83`](https://github.com/Norbyte/lslib/tree/6f5f6987d10d8485876b8368431c948e4dea4f83), supports DOS/Enhanced Edition/DOS2/BG3 packages, resources and story. Retain the current attributed container code; extend using [Localization.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Localization.cs) and [save/story helpers](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Save/SavegameHelpers.cs). Bounded managed ports with MIT notices are suitable; unrelated native/model libraries are unnecessary. High transfer for independently verified framing; PS5 story semantics unverified. It does not solve NewAge gameplay interpretation.
- **Norbyte/bg3se**, [`89a4bebcd1dad509692259a82a24cb4e806b65d1`](https://github.com/Norbyte/bg3se/tree/89a4bebcd1dad509692259a82a24cb4e806b65d1), [component definitions](https://github.com/Norbyte/bg3se/tree/89a4bebcd1dad509692259a82a24cb4e806b65d1/BG3Extender/GameDefinitions/Components): PC runtime concepts for inventory, approval and resources. RAM structs are not disk layouts. [Licence](https://github.com/Norbyte/bg3se/blob/89a4bebcd1dad509692259a82a24cb4e806b65d1/LICENSE) is **MIT plus Commons Clause**, not unrestricted MIT. Resolve permission or independently derive disk behavior rather than treating it as a permissive implementation dependency. Medium semantic/low offset transfer; only component-family presence corroborated here.
- **Sibling editors:** [NovFR DOS2 editor, `1a80ac7e6715d6d80abfe292398e6e6bbc53cc50`](https://github.com/NovFR/DoS-2-Savegame-Editor/tree/1a80ac7e6715d6d80abfe292398e6e6bbc53cc50) is an LSLib front end; no repository-wide licence identified. [AnthonyZJiang DOS Enhanced Edition editor, `c2dfb01fe9227092c52d9a0177998d767736c849`](https://github.com/AnthonyZJiang/D-OS-Save-Editor/tree/c2dfb01fe9227092c52d9a0177998d767736c849) explicitly limits its game/version; licence classified nonstandard/unreviewed. No proposed ports. PC UX precedent, low BG3 semantic transfer, not sample-tested. Shared studio does not establish a common live-state schema.
- **Official Toolkit / Multitool:** [Larian icon guide](https://docs.baldursgate3.game/Adding_Skill_and_Item_Icons) establishes root-template icon linkage. [ShinyHobo Multitool](https://gitlab.com/ShinyHobo/bg3-modders-multitool) offers indexing/extraction but explicitly states it is not open source. Optional human-operated preparation after licence review, never plugin runtime tooling. PC game-data concepts likely transfer by build; actual PS5 asset equality unverified. No downloaded executable run.

### Game data sources (names, descriptions, icons, limits, quests)

- **Preferred route: owner-supplied matching game-data pack.** [Game-data reader reference](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/bg3parser/gamedata.py) identifies root resources in Shared/Gustav/GustavX PAKs and language LOCA. Formats: LSPK/LSF/LSX/text/LOCA; prospective coverage includes shipped/mod templates, stats, classes, spells and journal definitions. Code licence unresolved; extracted records/images remain rightsholder content regardless of extractor licence. Default to local user-supplied packs, not a committed full dump. No installed game-data dump was available, so exact names/icons/variant joins were not verified here.
- **Required identity pipeline:** entity-instance GUID → current item/container → LSF Creators/Item → local template and Stats → static template/lineage → DisplayName/Description handle → language LOCA. Keep entity, local-template, static-root UUID, Stats and translation-handle namespaces separate. Runtime CurrentTemplate can be save-local. One Stats string can describe several named variants: return candidates/ambiguity, never mark the first/base match Verified. Our sample verifies Stats strings exist, not the static bridge. Store source, patch, language, module load order and confidence alongside resolutions.
- **Names/descriptions/categories:** templates provide MapKey, Stats, ParentTemplateId, DisplayName, Description and Icon; stats `using` inheritance supplies category, Slot, rarity, weight and effects. [Modding data-search guide](https://github.com/doombubbles/bg3-mods/blob/main/docs/BG3_BASE_GAME_DATA_SEARCH.md), accessed 2026-10-28, corroborates formats and inheritance; no code port/licence assumed. Resolve both inheritance chains and patch/mod overrides with cycle/depth guards. Localization markup and substitutions need safe display; unresolved expressions must not become invented calculated effects. High potential coverage, actual completeness/build match unknown.
- **Localization:** [MIT LSLib LOCA reader](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Localization.cs) defines handles, versions, header and text ranges. Use a bounded port with language/handle-version metadata and fallback, not English-only labels. No real LOCA join verified here.
- **Icons:** [official documentation](https://docs.baldursgate3.game/Adding_Skill_and_Item_Icons) and [wiki item-icon guide](https://bg3.wiki/wiki/Modding:Creating_Item_Icons), accessed 2026-10-28, describe root Icon names, controller/tooltip textures and atlas UVs. Resolve icon ID → texture bank/atlas and crop → DDS decode → PNG/WebP; do not assume an icon ID is a filename or all textures share dimensions. Default to user-supplied images or separately permitted assets; MIT extractors and CC text do not license game artwork. No icon bytes available/verified. Placeholders must retain complete functionality.
- **bg3.wiki:** [Gold](https://bg3.wiki/wiki/Gold), [Approval](https://bg3.wiki/wiki/Approval), item/spell/quest/location and modding pages offer readable descriptions, categories, static UUIDs, some Stats identifiers and images. Gold's static UUID and `OBJ_Goldpile` label cannot automatically replace case-sensitive save strings. HTML/MediaWiki export is a research route; the attempted Cargo API help fetch failed, so stable bulk JSON/schema/rate policy is unverified. Prefer a reviewed offline mapping; plugins cannot scrape at runtime. High human-reference value, medium ID coverage, lower mod/local-variant coverage; no exact wiki UUID→PS5 item join verified.
- **Licensing correction:** [wiki copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights) and [licensing explanation](https://bg3.wiki/wiki/bg3wiki:Licensing), checked 2026-10-28, include BY-SA/BY-NC-SA dual licensing and legacy exceptions. Preserve revision/author/attribution/change/licence metadata; isolate a share-alike data pack and resolve noncommercial-only records before redistribution. [Image notice](https://bg3.wiki/wiki/Template:License_larian) identifies game-owned assets. Runtime fetching does not grant rights; any user-configured acquisition service belongs in Core and needs permitted sources. User-owned extraction avoids bundling assets but does not imply redistribution rights.
- **Limits:** derive legal choices/ranges from matching stats/progression/resource definitions plus experiments. Storage widths, UI caps and plausible-value parser validators are not game rules. Distinguish base/effective ability, current/max/temp HP, total/class XP, stack amount/group encoding. Approval can be negative: [wiki mechanics](https://bg3.wiki/wiki/Approval) refute treating an upstream observed 0–100 sample range as its legal range. **No universal max-stack or safe quantity cap was verified.** Unknown limits disable writes or restrict them to proven cases.
- **Quests/collectibles/locations:** `Mods/GustavDev/Story/Journal/{quest_prototypes,objective_prototypes}.lsx` links QuestID/ObjectiveID to text; [game-data resolver](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/bg3parser/gamedata.py) also identifies subregion localization, classes/feats and action resources. Plan journal, waypoints, recipes, inspiration and illithid progress rather than a fictitious universal collectible count. Our sample has journal/waypoint structures only; completion semantics unverified. Map imagery/coordinate transforms need a licensed/user asset manifest; do not scrape commercial maps.

### Integrity / crypto

- **Outer integrity:** [MIT LSLib PackageWriter](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs), local `LspkIntegrity.cs` and `INTEGRITY.md` establish MD5+1 for the supported path. Independently reproduced on our sample. Preserve physical and file-list orders, untouched compressed entries and unknown LSF bytes. One sample does not distinguish every ordering hypothesis; obtain an archive where physical and case-insensitive name orders differ before generalizing.
- **LSMF:** [Wahkah write experiments](https://github.com/Wahkah/BG3SE/blob/fb4b9d15068b3f48a630c2d68922c3a732f32699/README.md) suspect u64 at +8; DanFinch research calls it a per-save identifier and retains older contradictory wording. Neither proves PS5 authentication. Preserve it; do not zero it or assume a missing key. Wrong packed-record/owner selection or dependent state can also explain failed loads. Need native-version baseline, metadata-only edit, one proven live-value edit and game-generated differential saves.
- **Osiris:** [MIT LSLib helpers](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Save/SavegameHelpers.cs) support separate StorySave.bin and older embedded Story; [parser research](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/bg3parser/osiris.py) describes typed databases and XOR-obfuscated strings. XOR is not a save-encryption key problem. Verified only: separate expanded story entry/header text, not full PS5 story semantics/checksum coverage. Prefer bounded MIT-derived reading; preserve compiled network/goals/adapters for future targeted writes.
- **Story joins:** upstream research identifies DB_Players, DB_Avatars, DB_PartOfTheTeam, DB_ApprovalRating, relationship flags, rest counters and waypoint facts. Accepted-minus-closed suggests active quests; closed includes failure, while current objectives reside in LSF Journal. Missing approval tuples mean unknown/no recorded event, not zero. Romance is flags/events, not an approval slider. These mappings were not validated against our PS5 story payload.
- Input is already decrypted. No additional payload key/signature algorithm or resigning mechanism was established/needed for reading. Console restoration stays the human's save-manager workflow. A parser reopen and repaired outer digest are not an in-game acceptance test.

## Vision

- **Overview:** campaign/build/ruleset/mod identity, thumbnail, localized region, reference-pack match and per-feature evidence status.
- **Characters/Party/Camp:** attributable portraits, avatars/companions/camp followers, base/effective abilities, HP/temp HP, class history/XP, feats, spell books/preparations/resources. Multiplayer avatar selection scopes approval and ownership. Metadata fields remain explicitly labeled.
- **Inventory/Equipment:** nested bags/chest; named/icon rows and descriptions; category/rarity/weight filters; IDs, quantity and confidence; equipment panel marks uncertain slots. Gold totals exclude world/trader stock and avoid recursive double counting.
- **Spells/Build:** search by name/class/level/source; distinguish learned/prepared/item-granted powers; later adjust only supported existing choices with dependency validation.
- **World/Journal:** spoiler-hidden objectives, waypoint/region lists, recipes, inspiration/illithid progress, camp events and relationship inspection. Optional maps require verified coordinate transforms and permitted assets.
- **Actions/Review:** first existing proven stack quantities, later selected-item refill to evidenced caps, target gold across eligible existing stacks, reversible transfers and supported build presets. Preview affected entities/dependencies; semantic before/after diff, undo/redo, raw explorer and export verification. No generic “max everything” or “complete all quests.”

## Phased plan

### Phase 1 — bounded PS5 reader and identity foundation

Goal: trustworthy structure without gameplay writes. Size **L**; risk **medium**.

- [ ] Independently implement LSMF directory, arena pointers, ownerlists, heap/string/enum pools and duplicate EntityId handling; preserve unknown types/versions.
- [ ] Add overflow/range/alignment/budget guards and unsupported-variant diagnostics; never hardcode component counts or one universal record-prefix rule.
- [ ] Decode LSF Creators/instance relationships; distinguish body/stats/spell/party/container entities. Stable value IDs include identity and occurrence provenance.
- [ ] Test identical-build avatars and raw fallback when a semantic join is ambiguous.

Evidence: additional native-build tutorial/Act 1–3 saves, GUID endian vectors, licence clearance or independent implementation record. Exit: supplied directory matches measured census, malformed pointers fail safely, no-op exact.

### Phase 2 — offline names, descriptions, icons and rules

Goal: useful meanings with honest ambiguity. Size **L**; risk **medium**, chiefly data/licensing.

- [ ] Specify reference-pack build/language/module/load-order/hash/rights manifest and bounded schema.
- [ ] Prepare selected PAK/LSF/LSX/LOCA/stats data outside plugin runtime; resolve inheritance/overrides/cycles.
- [ ] Generate candidate mappings, descriptions, class/spell/quest/location indexes and optional converted icons; preserve unresolved IDs.
- [ ] Add Core/SDK pack injection; use a permitted embedded subset until available.

Evidence: matching user-owned game data, non-English language and mod override, rights decision. Exit: every resolution has a source/ID/confidence; no missing image prevents use.

### Phase 3 — full read-only character, inventory, camp and story views

Goal: substantial value before writes. Size **L**; risk **high** for attribution.

- [ ] Decode current containers/recursive bags, stack groups and per-member sums; detect stale/transient state and cycles. Resolve gold identity/owner.
- [ ] Read abilities/HP/classes/progression/feats/spells/resources; distinguish computed caches and authoritative choices.
- [ ] Corroborate equipment with multiple signals; test stale flags, duplicate copies, ring/dual-wield ordering and virtual torch/instrument slots.
- [ ] Show LSF journal first, then bounded Osiris roster/approval/camp/relationship inspection; add recipes/waypoints/inspiration and optional maps.
- [ ] Keep ambiguous ownership/values read-only, show coverage, and bound all lazy pages.

Evidence: known-state sheets/loadouts and R10/R11 fixtures/screenshots. Exit: reads match observations; unsupported semantics never become Verified.

### Phase 4 — existing stack quantities and gold

Goal: first useful gameplay writes. Size **M–L**; risk **high**, conditional on console load gate.

- [ ] Prove item→group→member/amount references; start with one settled non-quest stack and unchanged structure/length.
- [ ] Add internal ScratchBuffer replacement; reuse existing LSF/LSPK preservation, recompression and digest paths; verify intended-byte diffs.
- [ ] Reject ambiguous/unsettled groups, unsupported counts and zero/removal semantics; limit to positive, tested values.
- [ ] Add existing gold-stack controls, then atomic target-total distribution across eligible stacks, with owner/quantity preview, totals and undo.

Evidence: gold ±1, consume/split/merge/transfer settled pairs and baseline/edited PS5 loads. Exit: correct effect survives game resave/re-export; unrelated owners unchanged. Failed loads keep writes disabled.

### Phase 5 — existing character values and spell selections

Goal: validated character improvements without an unproved full respec. Size **L**; risk **high**.

- [ ] Select one authoritative value with a differential fixture, such as current HP; document demonstrated dependent caches.
- [ ] Model base/effective boosts, class XP/progression and spell sources; never patch a computed stream solely because numbers look plausible.
- [ ] Enable each field/preset only after per-field console validation; prepared-spell changes require proven array/reference preservation.

Evidence: damage/heal/temp HP, boosted gear, level-up/respec and spell-preparation pairs. Exit: rest/combat/level-up/reload maintain intent and unrelated characters.

### Phase 6 — structural inventory/equipment operations

Goal: move/split/merge/add/remove with full undo. Size **L**; risk **very high**.

- [ ] Prove arena/reference relocation and structural preservation independently of item changes.
- [ ] Begin with existing-item transfers, then split/merge; validate acyclicity, ownership, stack entries, equipment effects and exclusivity.
- [ ] Add/remove only after unique entity allocation, template lineage, flags and cross-resource references are modeled; exclude unmodeled quest/unique items.
- [ ] Add structural transaction history and affected-object previews before exposing creation/equipment pickers.

Evidence: drop/pickup, trade/buyback, nested bag/chest movement and equip/unequip pairs, duplicate items and multiplayer. Exit: use/trade/rest/reload succeed; undo restores exact original.

### Phase 7 — narrowly scoped world/story operations

Goal: proven unlock/repair operations, broad browsing remains read-only. Size **L**, optional; risk **very high**.

- [ ] Select one recipe/waypoint operation with documented prerequisites and event side effects.
- [ ] Preserve typed Osiris facts/indexes/network and Journal consistency; determine whether an in-game event is required.
- [ ] Consider approval only after comparing ECS ratings, Osiris tuples and avatar/event behavior. Relationship presets require a separately proven state machine and may remain unavailable.

Evidence: quest accept/advance/complete/fail, positive/negative approval, rest/romance, waypoint/recipe differential saves. Exit: named operation survives dialogue/travel/rest/resave; a changed journal label is never claimed as quest completion.

## Requirements

| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
|---|---|---|---|---|---|
| BG3-R1 | Samples | Same native PS5 build/slot before/after gold ±1, potion use, split/merge/transfer; second settled save after rearranging plus no-action control. | P1/3/4: establish quantity/group/owner semantics. | Yes for stack writes | Current snapshot supports structure only. |
| BG3-R2 | Human | Owner restores spare slot, tests untouched/metadata/live-edit cases, checks effect, rests/trades, resaves and supplies redump plus build/ruleset/mod log. | P4–7: engine acceptance. | Yes for compatibility claims | Read-only gameplay; retain Experimental primitives. |
| BG3-R3 | Data | Build/language/module-matched template lineage, stats/inheritance, LOCA, class/progression/spell/feat/resource/journal/location tables; optional icons with rights manifest. | P2/3 and later legal limits. | Yes for comprehensive named UI | Raw IDs/permitted embedded subset. |
| BG3-R4 | Core | Import/validate/cache immutable packs outside save directories; key by build/mod/language/hash; scoped data/assets, size/path guards, exclusion from export. | P2: plugin cannot perform I/O; save overlay is not reference storage. | Yes for rich private packs | Embedded resources. |
| BG3-R5 | SDK | Structural command undo token/snapshot contract with stable IDs and deleted-binding restoration. | P6: ActionOutcome only has scalar Changes; Undoable=false clears history. | Yes for structural undo | Defer structural actions. |
| BG3-R6 | SDK | Optional reference-data context injection separate from save Sibling/Directory and FileOutput. | P2: Open currently receives save context, no pack resolver. | Yes for dynamic packs | Compile permitted data into resources. |
| BG3-R7 | UI | Icon/name/rarity/description cells/details, confidence/warnings, keyboard access/placeholders. | P2/3: TableView renders only field/text; ImageNode cells are blank. | No for text MVP | Separate lazy ImageNode/details. |
| BG3-R8 | SDK | Paged/filterable reference picker with IDs, labels, icons, provenance and allowed-choice rules. | P5/6: EnumSpec options are static/inline, no rich catalog query. | Yes for large creation picker | Small enum/text parameters; defer additions. |
| BG3-R9 | UI | Character/avatar selection, nested containers, category filters, optional equipment/map controls. | P3/6: no grid/map/domain interaction contract. | Only for target UX | Tabs/lazy sections/text tables. |
| BG3-R10 | Samples | Act 1–3/camp, hireling/origin/Durge, identical-build multiplayer avatars, bags/chest, rings/dual wield/torch/instrument and duplicate items. | P1/3/6: identity/equipment edge cases. | Yes for breadth | Restrict supported configurations. |
| BG3-R11 | Samples | Damage/heal/temp HP, boosting gear, learn/prepare spell, level-up/respec; quest completion/failure, approval ±, rest/romance, recipe/waypoint pairs. | P3/5/7: authoritative/dependent state. | Yes per write feature | Read-only with uncertainty. |
| BG3-R12 | Tooling | Independent bounded LSMF spec/reader; GUID/pointer/packed-stream vectors, census and malformed heap/owner fixtures. | P1: nominal rows are not universal field layouts. | Yes | Scratch directory probe only. |
| BG3-R13 | Data | Clarify DanFinch/Wahkah/Serofix licences before adaptation; retain MIT notices; avoid GPL/Commons-Clause imports without explicit resolution. | All phases: permissive-dependency rule. | For reuse, not independent work | Implement observed facts; use MIT references. |
| BG3-R14 | Tooling | Differential manifest: build/platform/ruleset/mod/action/control/expected values; diff ignores relocation while retaining unknown-byte accountability. | P1–7 reproducible validation. | Yes for sign-off | Manual scratch reports. |
| BG3-R15 | Data | Evidence-backed limits/equipment/progression/stack exceptions, with explicit unknowns. | P4–7: widths/UI caps are not game limits. | Yes per feature | Disable unknown writes. |
| BG3-R16 | Core | Optional pure action preview returning affected entities/dependencies/warnings before mutation. | P4/6/7: InvokeAction mutates and confirmation is static. | For full bulk preview UX | Computed read-only summary/separate preview action. |
| BG3-R17 | UI | Semantic before/after diff with owner/source/confidence; derived totals refresh; metadata/gameplay distinction. | P4–7: modified-cell styling alone is insufficient. | No for scalar MVP | Undo and plugin summary. |
| BG3-R18 | SDK | Bounded page/filter/query contract for catalogs/story, stable IDs across lazy expansion. | P3/6: TableNode sends all rows; client virtualization is not server paging. | No initially | Hierarchical lazy pages; <~5,000 rows/table. |
| BG3-R19 | Human | Decide asset/commercial redistribution scope, legacy wiki use, spoiler default and write evidence standard; manually promote Stable. | P2/3/release. | Yes for bundled assets | User assets/placeholders; hidden spoilers. |
| BG3-R20 | Data | Optional map assets, region transform and markers with provenance/rights. | P3 optional map view. | Only maps | Localized waypoint/region list. |
| BG3-R21 | Tooling | Offline deterministic pack builder, inheritance/override resolution, DDS conversion and budgets; no downloaded runtime tools in plugin. | P2: game PAKs may exceed save-oriented 256 MiB caps. | Yes for scalable packs | Human extracts selected small resources. |
| BG3-R22 | Core | Structural history/replay and memory limits coordinated with R5; reuse overlay conflict/reopen/backup-mirroring checks. | P6: DocumentSession history is scalar replay. | Yes for structural guarantee | Scalar edits inside one .lsv; no redundant multi-file redesign. |

## Acceptance criteria

- [ ] Implementation stays in plugin folder unless separate framework/UI work is scheduled; no plugin hooks/build tooling or committed saves/dumps. Keep C# maintainable and within applicable size limits.
- [ ] Decoder rejects overflow/out-of-range/cyclic references, unsupported variants and budget excess; preserves unknown components; tests dynamic counts and duplicate GUIDs.
- [ ] Supplied sample matches the independent census. Each Verified semantic field cites differential/in-game evidence, beyond descriptor presence.
- [ ] Identity survives duplicate classes/items, bags/chest transfers and multiplayer; ambiguous names/owners/slots stay explicit/non-editable.
- [ ] Inventory/gold match known observations; exclude trader/world gold, avoid recursive double counting; test multi-contribution groups/transient state.
- [ ] Distinguish base/effective abilities, HP/temp HP, XP/class levels and learned/prepared/item-granted spells; computed caches are not treated as authoritative setters.
- [ ] Missing/mismatched reference data never prevents opening; retain raw IDs, manifest/licence/language/module provenance. No data-pack asset enters exported save files.
- [ ] Bounded safe image conversion and text rendering; no executed wiki/game markup; placeholder and keyboard paths cover operations.
- [ ] Journal/closed-failed status, approval by avatar and relationships match observations; missing rows do not become fabricated zero/unlocked/completed values.
- [ ] No-op and edit→undo restore exact originals; modified exports preserve unknown data and untouched compressed/expanded payloads as promised.
- [ ] Every enabled write passes intended-byte/semantic diffs, edit→serialize→reopen, invalid-batch atomic rollback and undo/redo. Synthetic boundary tests run without samples; dependent tests skip when absent.
- [ ] Native untouched, metadata-only and each live-edit case tested separately on PS5; log warning/load/effect and verify after resave plus relevant rest/trade/combat.
- [ ] Existing conformance/primitive tests continue to pass when implementation occurs. This planning task deliberately ran no builds/tests.
- [ ] Initial views remain lazy/bounded; agree measured Act 3 opening/expansion/memory budgets with hardware/fixture sizes, rather than universal timing claims.
- [ ] Structural actions require allocation/relocation/reference consistency and complete undo proof. Story actions require their own prerequisites/side effects; no generic quest/romance shortcuts.
- [ ] README records pinned sources/licences, feature confidence, build/mod/ruleset scope, human PS5 evidence and SDK requests; human alone marks Stable.

## Open questions & risks

- Which precise PS5 build/ruleset produced this snapshot, and which game-data build matches? Directory agreement is not complete cross-version/cross-region proof.
- Can character→stats/spell joins avoid adjacent-entity, largest-book, owner-rotation and class-formula heuristics? Identical builds, gear boosts, summons and hirelings can defeat plausibility. Preserve uncertainty.
- Does LSMF u64 +8 identify a save, participate in validation or serve another purpose? Neither upstream disagreement nor failed loads proves a checksum/key/signature. Outer MD5 repair does not resolve it.
- Upstream documentation has corrections/conflicts: TotalSupplies was described as cache/junk before a prefixed value interpretation; Stats progressed from simple rows to packed streams. Independently test one pinned interpretation instead of merging incompatible claims.
- Are quantities in deferred stack-update states recoverable? If not, require a settled resave and explicit unknown status rather than guessing.
- Can saved template lineage disambiguate every Stats/name collision? No full game-data dump was supplied, so no exact catalog coverage percentage is justified.
- Console censoring/mod availability/version skew may affect content. [Larian mod/cross-save guidance](https://forums.larian.com/ubbthreads.php?Main=119514&Number=951934&ubb=showflat) requires console-compatible mods for relevant transfers; service support is not edited-save certification.
- Approval/romance spans representations and event history. Numeric agreement does not prove dialogue/camp scheduling or multiplayer behavior.
- Osiris is a versioned, large parser/performance surface. Measure simultaneous LSF/story/reference-pack memory; do not simply remove current caps.
- No installed game data, icon join, complete static catalog, max-stack table, full PS5 Osiris parse or edited-console save was verified. Network API reliability, asset permissions and code licences remain specific unresolved routes.
- Existing FileOutput supports multiple existing files, export detects collisions/reopens overlays and mirrors matching backup twins. Immediate gameplay changes are within one .lsv; framework requests should target real context/history/query gaps.
