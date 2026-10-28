# XorJson — improvement plan

## Summary

XorJson currently losslessly edits primitive tokens in byte-minus-one settings JSON and synthetic XOR-1 JSON. In the supplied PPSA16064 archive it proves the 3,978-byte GlobalSaveDataDefault/ue4savegame.dpx.sav transform and exposes masterVolume, while the 40,062-byte Character*/ue4savegame.dpx.sav is deliberately declined as plain FCharacterSaveV1 JSON. “Massively better” means a version-aware semantic editor for the offline hero (stats, equipment, item effects, quests, collections and world state) plus curated settings, while retaining a raw lossless fallback and unknown fields. The top blockers are the single early-game PS5 snapshot with no differential or in-game acceptance evidence, the missing structural JSON editing model, and the licensing/asset route for names, descriptions, limits and icons. No checksum or cryptographic operation is visible in these decrypted JSON files, but that must be proved with controlled corruption and console tests before enabling broad writes.

## Current state (evidence)

- plugins/XorJson/SaveToolkit.Plugins.XorJson/XorJsonPlugin.cs probes any valid transformed JSON by format only; it does not require the PPSA16064 path and registers format.json-xor.
- JsonEncoding.cs tries XOR-1 and byte-minus-one, rejects ambiguity, and fully parses the candidate. The sample establishes byte-minus-one for settings; XOR-1 support is synthetic.
- JsonTokenTree.cs records token offsets, ordered nodes and primitive specs with 16 MiB/64-depth/250,000-node bounds. It has no object/array insertion, deletion, identity references or semantic schema.
- JsonSaveDocument.cs exposes a bounded lazy raw tree and patches only changed primitive tokens. It preserves spelling, whitespace, duplicate keys, unknown values and no-op bytes; every output is reparsed. It cannot safely add/remove inventory entries or recompute cross-field dependencies.
- SaveToolkit.Plugins.XorJson.Tests/XorJsonTests.cs verifies the two transforms, masterVolume=50, plain-character rejection, exact no-op/undo bytes, malformed input, numeric limits, atomic edits and lazy paging. These are format tests, not console acceptance tests.
- plugins/XorJson/README.md records the verified 3,978-byte settings file and 40,062-byte plain character file, says game semantics/ranges are unknown, and documents no discovered checksum/compression.
- docs/SAMPLES.md lists PPSA16064 as Character*/ue4savegame.dpx.sav plus GlobalSaveDataDefault; there is no separate docs/formats/PPSA16064.md. The archive has one character and one global snapshot, not before/after pairs.
- Direct sample inspection (read-only Python in scratch): character root keys are SerializeMeta and CharacterSaveV1; HardFormat=FCharacterSaveV1, SoftVersion=2, FormatHash=3777917472, IsOnline=false, level 1, five inventory entries, nine attributes, 49 quests, achievements/collections and world-exploration structures. Item IDs include SW.Item.Sword, SW.Item.Bow and three artifacts; no effects are present in this early sample.
- The global decoded object has seven named blobs: Audio, General, Gfx, Controls, KeyBindings, Interface and Accessibility. It includes typed settings such as masterVolume=50, textLanguage=de-DE, displayMode=Fullscreen, brightness=2.190000057220459, deadzones and accessibility toggles.
- src/SaveToolkit.Sdk/Views/ViewNode.cs has sections, tabs, fields, tables, stats, actions, images and hex pages, but no item-picker/reference node, map node, row insertion/deletion protocol or field dependency metadata.
- src/SaveToolkit.Sdk/Values/FieldSpec.cs supports scalar ranges, enums and flags; BoundSaveDocument.cs provides atomic value batches and derived changes, not structural transactions.
- src/SaveToolkit.Sdk/ISaveDocument.cs can return multiple FileOutputs, but a document is opened for one file; cross-file edits need an explicit design. DocumentSession.cs records value-level undo and treats non-undoable structural actions as history-clearing.
- web/src/components/document/ViewRenderer.tsx, TableView.tsx, FieldControl.tsx and ActionButton.tsx render the current vocabulary (including searchable/virtualized tables and images), but have no item search/picker, image cards, map, diff, row editor or dependency-aware validation UI.
- src/SaveToolkit.Core/Export/WorkspaceExporter.cs reopens rewritten files and mirrors identical backup twins, so the plugin can retain its current lossless export path while semantic work is added.

## Research findings

### Existing editors/tools (same game, other platforms)

- [IshiakiZ/mcd2-save-editor, commit 84ffee67b73e49d5b888e31257ce9767aa8ab826](https://github.com/IshiakiZ/mcd2-save-editor/tree/84ffee67b73e49d5b888e31257ce9767aa8ab826) and its [MIT license](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/LICENSE) are the strongest implementation lead. It targets Windows Steam loose Character<id>.sav and Xbox/Game Pass WGS blobs, not PS5. Its [codec](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/codec.py) documents byte-minus-one settings, plain hero JSON, number spelling preservation and byte-identical no-op saves; hero.py maps Ability.Attributes, Inventory.Entries, effects, rarity, power, slots and caps; story.py and world.py cover quests and exploration. Transfer confidence is high for field names and the plain-JSON family because the PS5 sample has the same root and paths; it is medium for semantics and writes because that editor documents tested hero SoftVersion 5, while this PS5 sample is SoftVersion 2. The sample verified the root, attributes, inventory and quest/world paths, but not effects, caps or a successful PS5 reload.
- [dungeons.tools save editor](https://www.dungeons.tools/2/save-editor) independently states that Steam heroes are plain JSON, identifies Ability.Attributes and Inventory.Entries, and separates GlobalSaveDataDefault settings. It is PC-only, source/license for its database and code are not stated, and it gives no PS5-transfer guarantee. Use as corroboration and a manual oracle, not code or embedded data.
- [EditMySave’s MCD2 page](https://www.editmysave.app/minecraft-dungeons-2) confirms the PC Character*.sav location, plain JSON heroes and a separate global settings file. Terms/data licence are not supplied; treat it as corroborating product behavior only.
- [Nexus MCD2 Save Editor 1.1](https://www.nexusmods.com/minecraftdungeons2/mods/159) reports PC Steam/Xbox editing of stats, items, rarity/power/count, effects, enchantments, icons and presets, and says IDs were checked against build 1.1.1.0. It has no PS5 evidence and no reusable source/data licence; use its feature list to shape acceptance tests, not as proof.
- [CutFlame/MCDSaveEdit](https://github.com/CutFlame/MCDSaveEdit) is prior art for the 2020 Minecraft Dungeons PC .dat editor and item image extraction; the repository is MIT, but it credits/depends on DungeonTools AGPL-3.0 and FModel GPL-3.0 (the [README/license section](https://github.com/CutFlame/MCDSaveEdit/blob/master/README.md) documents this). It targets a different game/save schema, so transfer-to-PS5 is low; port no code or dependencies, but copy product lessons (searchable inventory, backups, game-data lookup).

### Same engine / same studio

- Mojang’s [Unreal Engine spotlight](https://www.unrealengine.com/en-US/spotlights/how-a-small-team-at-mojang-studios-made-minecraft-dungeons-in-unreal-engine?lang=en-US) confirms the 2020 title used Unreal; [Minecraft Wiki’s game page](https://minecraft.wiki/w/Minecraft_Dungeons) records Mojang/Double Eleven and Unreal. This establishes useful studio/engine context, not a shared serializer: the 2020 editor’s .dat is not evidence for this sequel’s JSON.
- [Epic’s SaveGame documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/saving-and-loading-your-game?application_version=4.27) explicitly says games define custom SaveGame classes and console file layouts vary. [UeSaveGame documentation](https://explore.market.dev/ecosystems/csharp/projects/uesavegame) says standard GVAS begins with GVAS, while custom serializers require extensions. The PS5 files begin with JSON text (or shifted JSON), not GVAS; a generic Unreal GVAS parser is therefore not a solution. Transfer confidence for the general warning is high; no checksum/key claim follows from it.
- A secondary [Project Spicewood report](https://www.windowscentral.com/gaming/minecraft/minecraft-dungeons-sequel-may-be-brewing-under-the-codename-project-spicewood) says the sequel was reported as Unreal-based, and [a contemporary review](https://as.com/meristation/analisis/analisis-de-minecraft-dungeons-2-una-secuela-que-mejora-en-todo-el-original-f202609-n/) reports an UE4-to-UE5 jump. Treat engine version as unverified for the PS5 build; the sample’s custom JSON is the stronger evidence.
- The PS5 identity is corroborated by [ProsperoPatches PPSA16064](https://prosperopatches.com/PPSA16064), which names Minecraft Dungeons II and Mojang (EU), and [AnyPS5 discussion #8868](https://github.com/boykopovar/AnyPS5/discussions/8868). These identify the title only; neither documents save semantics.

### Game data sources (names, descriptions, icons, limits, quests)

- The upstream editor’s generated [items catalog](https://github.com/IshiakiZ/mcd2-save-editor/tree/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data), [effects](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json), [enchantments](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/enchantments.json) and [world list](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json) map SW.Item.*, SW.Effect.*, enchantment tiers and quest/station/door identifiers. The repository code/data is MIT at this revision; preserve attribution if adapting. The catalog is based on game files/community submissions, claims broad PC build 1.1.1.0 coverage, and is not verified against this PS5 build. Item names/IDs have medium transfer confidence; legal ranges and effects need PS5 differentials.
- [MetaBot’s MCD2 effects](https://metabot.gg/en/minecraft-dungeons-2/effects), [enchantments](https://metabot.gg/en/minecraft-dungeons-2/enchantments) and [item pages](https://metabot.gg/en/minecraft-dungeons-2/uniques) provide human names, tooltip descriptions, categories and tiers from datamined build 1.1.1.0. They do not publish a permissive code/data licence; descriptions should remain source-linked or be regenerated from an approved dump, not copied wholesale into an MIT plugin.
- [dungeons.tools’ database](https://www.dungeons.tools/) claims game-file-derived weapons, armor, artifacts, enchantments, quests and interactive map data; its [privacy notice](https://www.dungeons.tools/privacy) says game assets/data belong to Mojang/Microsoft. Coverage is useful for research and runtime links, but no redistribution licence is offered. Embed only independently authored identifiers/labels after permission review; fetch/display assets only through an explicitly approved user-supplied route.
- [Minecraft Wiki’s CC BY-SA 4.0 terms](https://foundation.wikimedia.org/wiki/Terms_of_Use?useformat=mobile) and the [CC BY-SA deed](https://creativecommons.org/licenses/by-sa/4.0/deed.en) permit attributed text reuse with ShareAlike. This does not license Mojang artwork. [Minecraft’s official usage guidelines](https://www.minecraft.net/en-us/usage-guidelines) retain Mojang/Microsoft rights and restrict redistribution of game assets. Use text only with attribution/share-alike compliance; do not ship icons/models.
- The upstream research [README](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/mcd2-research/README.md) distinguishes install-file findings, community datamines and inferences, and reports candidate caps (Emeralds 99,999, SpringStone 100, level 100, three vendor levels, 9,999 stack) plus 180 items/116 Uniques/34 enchantments. These are PC build/community claims, not PS5-verified legal ranges; expose as Inferred until differential saves and console tests confirm them.
- Names and descriptions are feasible from sourced catalogs; categories and IDs are feasible for the five sample items and upstream IDs; item images are feasible only as user-supplied/local assets or a separately licensed runtime source. Quest names/step prose and map labels require a sourced text table or human-provided game-data dump; the sample itself stores IDs/states, not English labels.

### Integrity / crypto

- Complete sample inspection verified that adding one to every stored settings byte yields valid UTF-8 JSON with one blobs array; XOR-1 yields invalid JSON. The character file is valid plain UTF-8 JSON. No compression, signature, checksum field, GVAS header or key material appears in either decoded document. This supports the existing byte transform, not console acceptance.
- The upstream [codec at 84ffee67](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/codec.py) independently documents the same shifted/plain pair and exact number-spelling preservation. Its PC writer is useful evidence, but a PS5 save-manager export is already decrypted and the plugin must not attempt PS5 container crypto.
- Integrity remains an open experiment: make one controlled scalar edit in a copied PS5 character and global file, export, load each on a spare console slot, and compare the game’s rewritten JSON. Also mutate one byte in a copy to confirm rejection behavior. Until then, keep structural and high-risk edits read-only and label “no checksum observed” as Unknown rather than claiming no integrity mechanism.
- No code requiring a key or proprietary decryptor should be ported. MCDSaveEdit’s old asset extraction cites an AES key placeholder and GPL/AGPL dependencies; that is unrelated to the decrypted JSON plugin and would create licensing/security risk.

## Vision

- Top-level tabs: Hero, Inventory, Quests, World, Collections, Settings and Raw/Unknown.
- Hero: identity, online/guest status, location, level/power, XP, emeralds, Echo Shards (SpringStone), enchantment points and three vendor levels. Show source paths and Confidence; allow only fields with verified caps/dependencies.
- Inventory: loadout strip (melee, ranged, four armor pieces, artifacts and talismans), searchable lazy table of all entries, category/rarity filters, stack/power/rarity/slot controls, effect/enchantment detail, raw TypeTag, and unknown-field preservation. Existing entries come before creation.
- Item controls: sourced item picker with ID/name/category, optional icon, duplicate/equip/delete actions, bounded power/stack ranges, rarity-aware effect slots, and a “keep game rules” validation mode. Unconfirmed IDs/effect tiers remain read-only or require an explicit warning.
- Quests/collections: quest and task state/progress table, focused quest, achievements, collected item groups and discovery counters. Bulk completion/reset actions are deferred until reward/side-effect behavior is understood.
- World: discovered stations, doors, cutscenes, gimmicks, fog-of-war pages and raw actor states; a map is a later UI enhancement and should not invent coordinates absent a licensed/source map.
- Settings: named Audio/General/Gfx/Controls/Interface/Accessibility sections with enums and conservative ranges, plus KeyBindings raw/advanced view. Keep byte-minus-one encoding and unknown blobs intact.
- Bulk actions: max plausible currency, set all selected stack counts, equip a loadout, discover stations, or apply a preset only after the corresponding field dependencies and undo model are proven.

## Phased plan

### Phase 1 — establish PS5 format/version evidence

Goal: separate proven read mappings from PC-only assumptions.

- [ ] Obtain at least two PS5 character snapshots (new hero and progressed hero), plus before/after pairs for one currency, one level/XP change, one item pickup, one equip, one rarity/effect change, one quest step, one map discovery and one settings change.
- [ ] Record title/content/build/region and whether each hero is offline; test current SoftVersion and FormatHash handling across snapshots.
- [ ] Build a read-only typed model over the existing token-preserving parser; keep an indexed raw fallback for every unknown path.
- [ ] Differentially identify stable item identity, effects, equipment slots, quest progress, achievements, collections and world state; reject row-position-only mappings.
- [ ] Add fixtures, corruption tests, version gates and byte-identical no-op tests without committing samples.
Evidence: PS5 before/after pairs and a human-confirmed field map. Risk: high (one early snapshot cannot prove semantics). Size: L.

### Phase 2 — curated settings and hero read view

Goal: deliver immediate semantic value without structural writes.

- [ ] Turn the seven global blobs into named sections with BoolSpec, conservative IntegerSpec, FloatSpec and EnumSpec values; preserve unknown blobs and number spellings.
- [ ] Add a character probe requiring SerializeMeta.HardFormat/CharacterSaveV1 and a version warning for untested SoftVersions; avoid competing with GenericJson on unrelated plain JSON.
- [ ] Expose hero metadata, attributes, achievements, collections and world summaries with Verified only where sample/differential evidence supports it; use Inferred/Unknown otherwise.
- [ ] Keep writing limited to existing scalar settings and low-risk hero scalars; re-open and compare unrelated JSON token bytes after every edit.
Evidence: settings before/after and console reload, plus PS5 field diffs. Risk: medium. Size: M.

### Phase 3 — inventory/effects semantic editor

Goal: make existing gear understandable and safely editable.

- [ ] Parse Inventory.Entries into stable item identities (generator seed plus occurrence, with index fallback), not array positions.
- [ ] Embed a small, attributed ID/name/category/effect catalog derived from permissively licensed or owner-supplied data; leave unknown IDs as raw tags.
- [ ] Add lazy inventory tables, equipment grouping, effect/enchantment cards and bounded validators for stack, power, rarity and slot combinations.
- [ ] Enable edits to existing quantities/power/rarity only after differential evidence and console tests; preserve all unrecognized fields and effect batches.
- [ ] Add item-level before/after fixtures and undo tests; defer add/delete and unique-effect synthesis.
Evidence: saves containing non-empty effects, rarities, enchantments, talismans and merchant/cosmetic entries. Risk: high (wrong identity/effect write can destroy gear). Size: L.

### Phase 4 — quests, collections and world

Goal: expose progression and collectibles with honest labels.

- [ ] Map quest.Quests, TaskData, FocusedQuestId, achievements, collections, minecart stations, doors, fog and actor states to lazy tables.
- [ ] Source quest/step names and map labels; show raw IDs beside any inferred label and document source/license.
- [ ] Add read-only diff views first; implement individual state/progress edits only after reward/soft-lock behavior is tested.
- [ ] Make map images optional/user-supplied; never ship Mojang artwork without permission.
Evidence: multiple progression states, quest reward observations and a supplied/licensed map dataset. Risk: high (soft-locks and unearned rewards). Size: L.

### Phase 5 — structural actions and maturity

Goal: add add/remove/equip/presets and promote only proven features.

- [ ] Design a token-preserving structural serializer for arrays/objects, stable IDs and unknown-field retention; keep no-op byte identity.
- [ ] Add undoable structural snapshots or a Core transaction API; do not silently clear history for ordinary inventory actions.
- [ ] Add SDK/UI item picker, references, add/remove row actions, icons and dependency validation, or document a plugin-local workaround.
- [ ] Run controlled console acceptance for each action, update README sources/licences and keep unsupported formats read-only.
Evidence: successful PS5 loads for each action and repeatable round trips. Risk: very high. Size: L.

## Requirements

| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
|---|---|---|---|---|---|
| XOR-R1 | Samples | PS5 before/after character saves for currency, XP/level, pickup, equip, effect/rarity, quest and world changes, with build/region metadata | Phase 1 mapping and every write claim | Yes | Read-only raw paths; existing one sample only |
| XOR-R2 | Human | Owner performs spare-slot PS5 reload tests and reports exact in-game results, including offline/online status and cloud-save behavior | Establishes console compatibility and maturity | Yes | Keep all semantic writes read-only |
| XOR-R3 | Tooling | Differential JSON tool that compares typed paths, token spelling/offsets, array identities and unrelated-byte preservation | Prevents guessing and regression | Yes | Handwritten scratch scripts, not maintainable |
| XOR-R4 | Data | Attributed embedded catalog for SW.Item, rarity, effects, enchantments, categories, caps and quest/map IDs, with per-entry provenance/version | Names, descriptions and validators in Phases 2–4 | Yes for semantic labels | Raw tags and source hyperlinks |
| XOR-R5 | Data | Explicit asset licensing route for icons/descriptions (owner-supplied dump, permission, or runtime link) | ImageNode/item cards without redistributing Mojang art | Yes for icons | Text-only cards and no images |
| XOR-R6 | SDK | Stable references between fields and records plus a structured row add/remove/replace outcome | Inventory identities, equip relationships and structural edits | Yes for add/delete | Existing-value scalar edits only |
| XOR-R7 | SDK | Cross-field/dependency validation and warnings (level↔XP, rarity↔effects, slots, caps) | Avoid invalid combinations before serialization | Yes for safe writes | Plugin-local validation with conservative read-only fields |
| XOR-R8 | SDK | Transaction/snapshot undo for structural and multi-record actions | Bulk inventory/preset actions must be recoverable | Yes for actions | Mark action non-undoable and clear history (unsafe UX) |
| XOR-R9 | Core | Multi-file/session transaction semantics when a semantic action must update character and global state together | Future active-character/settings coordination and atomic export | No initially | Open and export files separately |
| XOR-R10 | UI | Searchable item picker with categories, icon/card slot, unknown-ID fallback and bulk selection | Inventory/equipment vision | Yes for usable editor | Generic table + scalar fields |
| XOR-R11 | UI | Rich table row controls, diff view, map/collection rendering and dependency warnings | Quests/world and safe review | No for first read view | Lazy sections, tables, TextNode warnings |
| XOR-R12 | Core | Local reference-data/asset pipeline with version/hash metadata and no plugin network I/O | Reproducible catalogs and user-supplied assets | No | Embedded labels only |
| XOR-R13 | Tooling | Fixtures for SoftVersion/FormatHash changes, malformed JSON, unknown fields, duplicate keys, numeric spelling and corruption | Version gate and never-lose-data guarantees | Yes | Current generic token tests |
| XOR-R14 | Samples | Progressed hero with effects/enchantments/talismans, merchant/cosmetic entries, multiple quests and world discoveries | Phase 3/4 coverage | Yes | Keep those sections raw |
| XOR-R15 | Human | Owner decides whether PC catalog text may be embedded, whether user-supplied assets are acceptable and which actions are in scope | Resolves licensing/product policy | Yes for data packaging | Link to external sources; no copied assets |
| XOR-R16 | SDK | Asset/Hex access must remain bounded and lazy for fog maps and large unknown trees | Avoid eager huge views | No | Existing SectionNode.Lazy and HexNode |
| XOR-R17 | Core | Export verification must reopen semantic outputs and mirror only byte-identical backups | Protects PS5 restore workflow | No (already available) | Current WorkspaceExporter |

## Acceptance criteria

- [ ] The plugin distinguishes shifted settings, plain FCharacterSaveV1, unrelated JSON and unsupported format versions without false positives.
- [ ] Every supplied sample opens; malformed/ambiguous/oversized/deep inputs are rejected without leaking save content.
- [ ] A no-op serialize is byte-identical for both settings and character samples, including number spelling and unknown fields.
- [ ] Existing scalar edits are atomic, bounded, undoable and round-trip through serialize/reopen; unrelated token bytes remain unchanged.
- [ ] Character views show metadata, attributes, inventory, quests, achievements, collections and world state lazily, with stable IDs and explicit confidence labels.
- [ ] Any item name, description, cap, quest label or icon has a cited source, version and licence route; unknown IDs remain visible as raw identifiers.
- [ ] Inventory/equipment edits are enabled only after PS5 differential evidence and a human spare-slot load test; add/delete/structural actions remain disabled otherwise.
- [ ] Structural actions, when enabled, preserve unknown fields, recompute any discovered integrity, produce a reviewable diff and have recoverable undo.
- [ ] Settings expose the seven observed blobs with conservative specs, preserve unknown blobs and pass a before/after console reload test.
- [ ] Tests cover transform vectors, version gates, corruption rejection, duplicate/unknown JSON, effects/identity, caps, atomic failures, lazy bounds and console-tested edits; sample-dependent tests skip when absent.
- [ ] README documents exact PS5 evidence, PC transfer limits, all sources/licences, integrity findings, maturity and remaining unsupported fields.

## Open questions & risks

- Is the “Minecraft Dungeons II” label and PPSA16064 mapping stable across all regions/patches, and does PS5 retain the same FCharacterSaveV1 schema after updates?
- Does FormatHash identify a schema/version that must be checked before writing, and are SoftVersion=2 and PC SoftVersion 5 semantically compatible?
- Are there hidden signatures, cloud metadata or console-side checks outside the decrypted JSON that reject otherwise valid edits?
- What exact item identity survives reorder/duplicate entries, and how are merchant stock, cosmetics, books, talismans and Unique effects represented in PS5 saves?
- Which PC caps and effect pools apply to the PS5 build, and does the game clamp, discard or repair out-of-range values?
- Can quests be advanced without granting rewards, achievements or creating soft locks; does setting a task state require matching parent sequence/selection states?
- Are fog-of-war bytes, actor states and door IDs portable across generated map versions, or do they need a build-specific world table?
- Can a permissive, redistributable source provide descriptions and icons, or must the plugin use user-supplied data and source links?
- How should the toolkit represent structural row changes, stable references, snapshots and multi-file transactions without weakening existing no-loss guarantees?

