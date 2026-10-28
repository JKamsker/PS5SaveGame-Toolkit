# Cyberpunk2077 — improvement plan

## Summary

The plugin already provides guarded PS5 2.21 editing of money, five attributes with mirrored stats, unspent attribute/perk/relic points and selected existing stacks. A substantially better editor should connect inventory instances to localized TweakDB records, show equipment/cyberware and the post-2.0 perk tree, and explain vehicles, travel points and quest progress. The top three blockers are one distinct early-game PS5 sample, missing versioned/licensed reference data, and unknown gameplay dependencies without console validation. Extend the existing preservation-oriented writer rather than replace it with whole-save serialization. Deliver named read-only views and safer existing-value actions before structural item creation or story changes.

## Current state (evidence)

- Research date: 2026-10-28. Read `AGENTS.md`, `docs/PLUGIN_GUIDE.md`, `docs/PLUGINS.md`, `docs/SAMPLES.md`, `docs/formats/README.md`, `docs/formats/PPSA04027.md` and [issue #1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1) through `gh issue view 1`. No repository changes, builds/tests or execution of downloaded programs were performed. Existing scratch downloads and samples were reused.
- All plugin source references below are under `plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077/`; tests are under `plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests/`.
- `Cyberpunk2077Plugin.cs` claims PPSA04027 gameplay `sav.dat`. GenericJson owns `settings.json` and `metadata.9.json`; `user.gls` is separate and unsupported. Slot summaries are not authoritative live gameplay state.
- `CsavContainer.cs`, `CsavTree.cs`, `CsavWriter.cs`, `BinaryData.cs`: bounded VASC/FZLC/4ZLX/LZ4 container, EDON directory, ENOD footer, virtual node offsets, tree/link checks and opaque-byte preservation. No-op output retains original compressed bytes; changed output is decompressed and reparsed.
- `InventoryReader.cs`: container ownership, matching duplicated 16-byte item prefixes, TweakDBID, seed, structure and optional quantity. Writes require save/game/archive 265/2210/195. Old CyberCAT's 15-byte item prefix is incompatible with this sample.
- `ItemNames.cs`, `Resources/ItemNames.json`, `CyberpunkDocument.cs`: 36 embedded record names/categories, CRC32-plus-length checked; all 22 sample identities resolve. These are internal identifiers, not localized names/descriptions/icons. Writable stacks are player-container, quantity-only 29-byte ammo/component/consumable records; extended items and other containers remain read-only.
- `ProgressionData.cs`, `RedPackage.cs`, `PackageClass.cs`, `PlayerStatsReader.cs`, `ProgressionEditor.cs`: package 4/flags 0/sections 6, owner-1 player handle, attributes and unique additive entity-1 forced-stat mirrors. Point edits can materialize omitted Int32 properties, repairing offsets while preserving opaque properties/chunks. Level/Street Cred/XP stay read-only because thresholds/rewards/unlocks are unverified. Attribute changes preserve points/perks independently.
- `FactsReader.cs`: validates packed count and parallel uint32 hash/value planes. `CyberpunkDocument.cs` shows raw facts, lazy 128-row pages, node tree and hex views. No fact naming, quest-state editor, perk allocation, equipment manager, garage manager or reference assets exist.
- Money 100,000,000, stacks 999,999 and points 100,000 are editor policies, not verified engine limits. Keep existing larger values representable; do not call these legal gameplay maxima.
- Inspected `Cyberpunk2077ConformanceTests.cs`, `InventoryEditingTests.cs`, `ProgressionSampleTests.cs`, `ProgressionValidationTests.cs`, `RedPackageFormatTests.cs`, `CyberpunkSampleTests.cs`, `CsavFormatTests.cs`. Coverage includes mixed edits, opaque preservation, omitted properties, independent patches, corrupt-input refusal and undo. README reports 62 passed; without samples 18 passed/44 skipped. These are recorded results, not tests rerun here.
- Independent verification reran the existing locally authored scratch `inspect_sample.py` under `python -I`: two gameplay entries, **one distinct save**, 1,302,721 stored bytes, 22 chunks, 5,607,178 expanded bytes, 159 nodes, 22 items and 2,750 facts across ten tables. A small independent Python LZ4 decoder expanded every chunk to its declared length. Verified archive bytes equal the existing scratch save. Money node 39 = 300; handgun/rifle/shotgun nodes 49–51 = 600/600/120. An identical backup is not additional gameplay evidence.
- Scratch `check_names.py` verifies all 36 embedded TweakDB hashes and 22/22 sample matches. Extended identities include grenades, fists, underwear, cutscene Unity and Streetkid clothing; ID recognition does not prove extended writable semantics.
- Independent FNV-1a/32 calculation: all **19,357** upstream Facts.json keys match their **exact** name strings; 1,129/2,750 sample facts have dictionary matches. Nine names have leading/trailing spaces: trimming changes their hashes. Preserve canonical strings separately from friendly labels. Debug-ticket names such as `CYB-*` are identifiers, not quest descriptions.
- Byte-search leads: EquipmentSystem, FastTravelSystem, CraftingSystem and PlayerDevelopmentData strings occur in the payload. This is not proof that all upstream records/properties exist. Directory nodes include JournalManager, PersistencySystem2, VehicleSystem and mappins; deeper semantics remain undecoded.
- Framework: `src/SaveToolkit.Sdk/Views/ViewNode.cs` already has tabs, lazy sections, tables, stats, actions, images and hex; `FieldSpec.cs` provides primitives/enums/flags; integer wire values preserve precision as strings. `ISaveDocument` already supports `GetAsset`, `FileOutput` for multiple files, actions and `StructureChanged`; sibling file access also exists. These are not missing primitives.
- Actual gaps: no semantic reference/selection/query vocabulary; `EnumSpec` embeds a full option list. `web/src/components/document/TableView.tsx` virtualizes and searches supplied rows but renders only text/field cells, blanking image/action cells. `FieldControl.tsx` uses a native enum select. `ActionButton.tsx` offers parameters/static confirmation, not a calculated before/after preview.
- `BoundSaveDocument.cs` provides binding rollback, not arbitrary model transactions. `src/SaveToolkit.Core/Sessions/DocumentSession.cs` validates fields and maintains value-change undo, clearing history for `Undoable=false`. `WorkspaceExporter.cs` reopens output through an overlay: parseability does not establish gameplay effects. Core also mirrors backup directories; test the final ZIP rather than promising backup twins are never changed.

## Research findings

### Existing editors/tools (same game, other platforms)

- **CyberCAT — MIT, PC; high framing transfer.** [Core 50db3263a94247531853ba13c2e200f2e784ba8d75](https://github.com/WolvenKit/CyberCAT/tree/50db3263a94247531853ba13c2e200f2e784ba8d75/CyberCAT.Core), [licence](https://github.com/WolvenKit/CyberCAT/blob/50db3263a94247531853ba13c2e200f2e784ba8d75/LICENSE). CompressionHelper, SaveFile and FactsTableParser explain compression, virtual offsets and fact planes. MIT portions may be adapted with notices. Sample framing/LZ4/facts agree; its older item schema differs. Do not port its 15-byte inventory prefix or assume modern gameplay schemas.
- **Deweh CyberCAT-SimpleGUI — MIT application, PC; strongest curated controls/dictionary lead.** [c1fd00305e7db905a4e7de8cfa360d4f4efee3b2](https://github.com/Deweh/CyberCAT-SimpleGUI/tree/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2), [licence](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/LICENSE). [SaveFileHelper](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Utils/SaveFileHelper.cs) traverses player/equipment handles and item-linked stats; inventory/player/quest/vehicle controls supply behavioral references. Money/attributes already agree with PS5 bytes; equipment/vehicles/level transitions are unverified. Its `.gitmodules` references GPL WolvenKit: MIT application licensing does not make dependencies permissive. Port eligible MIT portions with notices; independently implement binary schemas where necessary.
- **PixelRick CyberpunkSaveEditor — MIT, C++, PC and historical PS4w support.** [3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062](https://github.com/PixelRick/CyberpunkSaveEditor/tree/3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062), [licence](https://github.com/PixelRick/CyberpunkSaveEditor/blob/3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062/LICENSE), [2.12 release limitations](https://github.com/PixelRick/CyberpunkSaveEditor/releases/tag/v0.6.1-alpha.1). Tree/scriptable/inventory/modifier editing and diffing are useful leads; release warns about broken persistency parsing and nonworking vehicle spawning. [FactsTable writer](https://github.com/PixelRick/CyberpunkSaveEditor/blob/3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062/source/redx/csav/nodes/questSystem/FactsDB/FactsTable.h) sorts by hash. PS4w-specific flag fixes demonstrate platform differences. Sample fact planes agree; PS4 flags/extended writes unverified. MIT code is eligible after auditing bundled dependency licences.
- **Gibbed fork — MIT, early PC baseline.** [4c412a6447e740779009df1b28978c371d778109](https://github.com/gibbed/CyberpunkSaveEditor/tree/4c412a6447e740779009df1b28978c371d778109) is PixelRick ancestry, not independent modern tooling. README advertises fixed-size node hex edits. Useful historical reference, low 2.x semantic transfer; no new sample validation. No code port proposed.
- **CyberpunkPythonHacks — 0BSD, PC Python.** [Repository](https://github.com/fmwviormv/CyberpunkPythonHacks), [actual licence](https://github.com/fmwviormv/CyberpunkPythonHacks/blob/main/LICENSE), inspected 2026-10-28. Scriptable traversal and omitted/default fields are independent research leads. No upstream scripts executed or PS5 writes tested. Permissive source is eligible for reviewed offline helpers; avoid old fixed chunk-capacity assumptions. Some derivative gists call it ISC; use the actual pinned source licence, currently 0BSD.
- **Night City Save Editor — GPL-3.0-only, Rust/egui, Windows/macOS, recent 2.5 lead.** [README 5ef1112ad6d275cf1bb58421b96edbdf167c7c3b](https://github.com/desfpc/Cyberpunk25SaveEditor/blob/5ef1112ad6d275cf1bb58421b96edbdf167c7c3b/README.md), [notices](https://github.com/desfpc/Cyberpunk25SaveEditor/blob/5ef1112ad6d275cf1bb58421b96edbdf167c7c3b/THIRD_PARTY_NOTICES.md). Advertises progression, quantities, vehicles, facts and appearance presets; explicitly targets save/game 277/2500. Useful omitted-point-field/cosmetic rebinding research, not PS5 compatibility proof. Our 265/2210 sample cannot verify it. Do not copy/link GPL implementation under the current plugin rules; trace MIT dictionary sources separately.
- **PS4 commercial/homebrew tools.** [Save Wizard CUSA16596](https://www.savewizard.net/games/cusa16596-ps4-cheats/) lists money/components/ammo/level cheats; [FAQ](https://www.savewizard.net/faqs/) excludes native PS5 games. Proprietary feature list, no portable algorithm/licence; low PS5 2.21 transfer confidence and no sample validation. [Apollo PS4](https://github.com/bucanero/apollo-ps4) and [GPL patch database](https://github.com/bucanero/apollo-patches) provide PS4 export/patch infrastructure, not a verified PS5 Cyberpunk semantic parser. Existing scratch `CUSA16563.savepatch` says **Dusk Diver**: reject this unrelated-title false lead.
- **Cheat tables / 010 / platform breadth.** [Nexus table 6385](https://www.nexusmods.com/cyberpunk2077/mods/6385) edits running PC memory; author permissions restrict modification/reuse. No save-layout evidence or port proposed. [010 guidance](https://wiki.redmodding.org/cyberpunk-2077-modding/glossary) concerns legacy REDengine resources; no validated modern PS5 CSAV template found. Targeted GitHub, Nexus, PSX-Place and GBAtemp searches found no additional audited Xbox/PS5/Switch 2 parser or verified PS5 cheat layout. This is a bounded search result, not proof none exists. Commercial cloud-editor/service claims are not byte evidence.
- **Cross progression is a collection route, not byte identity.** [CDPR cross progression](https://support.cdprojektred.com/en/cyberpunk/pc/sp-technical/issue/2425/how-to-use-cross-progression-1), [PS4→PS5 migration](https://support.cdprojektred.com/en/cyberpunk/playstation/sp-technical/issue/2075/how-do-i-transfer-my-save-file-to-the-next-gen-update-1), [regional restrictions](https://support.cdprojektred.com/en/cyberpunk%20/pc/sp-technical/issue/2242/cross-progression-in-game-versions-with-regional-restrictions), accessed 2026-10-28. Game-managed transfers can produce comparison saves; they do not prove direct PC/PS4/Xbox/Switch 2 layout compatibility. No user credentials or cloud API integration is needed.

### Same engine / same studio

- **WolvenKit RED4 — GPL-3.0, PC REDengine 4, strongest modern schema catalogue.** [65ea74b014e11bb41659c65401e43d5ebe4d8d75 Save](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save), [licence](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/LICENSE). Inventory/package readers, JournalManagerParser and PersistencySystem2Parser give format leads; EquipmentSystemPlayerData, FastTravelSystem and vehicleGarageComponentPS identify relationships. Current prefix/player package framing is sample-confirmed; persistency/equipment/cyberware/garage mutations are not. Read schemas as format research; implement independently with binary tests or obtain another licence before adapting GPL code.
- **Post-2.0 perk fields are schemas, not data tables.** [gamedataNewPerk_Record](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/gamedataNewPerk_Record.cs) declares attribute, levels, requirement, requiresPerks, localized-name/description keys and perkIcon. Actual ranks/prerequisites require same-build TweakDB; allocation storage needs PS5 save evidence. GPL reference only, no code port; semantic transfer inferred.
- **Witcher sibling — same studio, different save grammar.** [Atvaark W3SavegameEditor cff905189d565606aed9058b2804eb4c5ee9f540](https://github.com/Atvaark/W3SavegameEditor/tree/cff905189d565606aed9058b2804eb4c5ee9f540) is an archived MIT unpacker using LZ4; [WolvenKit-7 explorer](https://github.com/WolvenKit/WolvenKit-7/blob/main/WolvenKit/Forms/frmSaveEditor.cs) cites it. Useful preservation/explorer patterns, very low direct RED4 semantic transfer. No Witcher bytes verified or sibling parser port proposed; copied sources would need their own licence audit.
- **Offline extraction.** [Deweh ResourceGenerator](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/ResourceGenerator/Program.cs) reads local TweakDB and WolvenKit string resources; [REDmod documentation](https://cdn-l-cyberpunk.cdprojektred.com/REDmod-docs.pdf) describes tweak files. Generator source is MIT but references GPL libraries; REDmod/game data are proprietary. User-run offline export is viable; no extraction tools executed here. [TweakXL licence](https://github.com/psiberx/cp2077-tweak-xl/blob/master/LICENSE) is MIT; useful record-inheritance context, not proof of persisted item state. No runtime plugin I/O is proposed.

### Game data sources (names, descriptions, icons, limits, quests)

- **Immediate dictionary expansion.** [Deweh items.bin](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/items.bin) supplies record strings; [ItemClasses.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/ItemClasses.json) supplies categories. MIT source provenance; current trimmed dictionary/notices already exist. Coverage is identifiers/categories, not localization/icons/limits. Hash exact UTF-8 using CRC32/IEEE plus byte length shifted 32 bits. Sample: 22/22 current items match. Preserve unknown full hashes and detect collisions; source-code licensing does not automatically license extracted game assets.
- **Same-build TweakDB + localization, preferred full-fidelity route.** [TweakDB documentation](https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/blob/main/for-mod-creators-theory/tweaks/tweaks/README.md), [gamedataItem_Record](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/gamedataItem_Record.cs). Binary database/archive exports can yield JSON records keyed by exact hex/decimal-string TweakDBID, locale/build/DLC and source hashes. Join displayName/localizedDescription LocKeys to language strings; itemType/quality/icon fields provide categories and asset references. Record quality is not necessarily final instance tier. Also obtain perk ranks/prerequisites, slot compatibility and progression curves. No verified complete max-stack/legal-range table is available: storage width and GUI caps are insufficient. Full PS5 data join unverified beyond current names. Proprietary data should initially be user-supplied; GPL schema code is reference-only.
- **Icons require multiple joins.** [Inkatlas format](https://wiki.redmodding.org/cyberpunk-2077-modding/for-mod-creators-theory/files-and-what-they-do/file-formats/game-icons-the-inkatlas-file) describes UIIcon → atlasResourcePath/atlasPartName → normalized UV crop → XBM texture. [Iconic weapon guide](https://wiki.redmodding.org/cyberpunk-2077-modding/modding-guides/items-equipment/adding-new-items/weapons/new-iconic-weapon-step-by-step) distinguishes inspect description from UIData flavor text. Export bounded thumbnails offline; retain atlas/part provenance and serve via existing GetAsset. PS5 mapping unverified. CDPR art is not MIT/GPL; use user-supplied assets and placeholders until redistribution permission is established.
- **Ink archive is an index, not an art pack.** [spuddeh/cp2077-ink-archive 2358b8d2d938d7b97613d26d488b3d99ed6057ac](https://github.com/spuddeh/cp2077-ink-archive/tree/2358b8d2d938d7b97613d26d488b3d99ed6057ac): JSONL/SQLite UI resource records and atlas relationships; README explicitly excludes textures, TweakDB and localization. MIT code, separate game-data rights. Useful discovery aid, not item-name/icon coverage alone; no PS5 reference-data validation.
- **Vehicles, travel and facts: concrete catalogues.** [Vehicles.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Vehicles.json) has 105 record strings. [FastTravel.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/FastTravel.json) has 232 marker/record pairs and IsEP1 flags, not a complete localized coordinate map. [Facts.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Facts.json) has 19,357 exact FNV32 names; 1,129 sample fact entries match. MIT source provenance supports attributed trimmed catalogues subject to game-data rights review. Counts do not establish 2.21 completeness, ownership or safe unlock recipes. Vehicle/travel stored joins are unverified.
- **Quest and collectible descriptions.** [Quest IDs](https://wiki.redmodding.org/cyberpunk-2077-modding/for-mod-creators-theory/references-lists-and-overviews/reference-quest-ids), documented 2025-08-20, provides HTML/path lists and downloadable journal JSON. Broad base/PL naming/objective lead. Journal paths/IDs need joins to journal hashes/node refs, not FNV fact names; PS5 join unverified. Treat extracted game text as proprietary unless a specific grant applies. Tarot/NCPD/gig completion needs journal/mappin/quest evidence; absent facts do not prove incompletion.
- **Completion-tracker hypotheses.** [defessler tracker README](https://github.com/defessler/cp2077-tracker/blob/master/README.md) combines metadata finishedQuests, facts, aggregator strings and manual overrides; useful parent/phase ambiguity examples. API reports no licence: no code/catalogue port without permission; independently verify hypotheses. Its PC observations/counts are not our PS5 evidence.
- **Wiki fallback with attribution.** [Unity article](https://cyberpunk.fandom.com/wiki/Constitutional_Arms_Unity) supplies HTML descriptions, base IDs, categories/images; [licence notice](https://cyberpunk.fandom.com/wiki/Category%3ALicense_templates) says community content is CC-BY-SA unless noted. Base IDs can map after exact Items.* hashing, but sample cutscene Unity is not automatically a listed default variant. An attributed share-alike text pack is possible subject to applicable notices; images need per-file rights and usually retain game-company copyright. No bulk scraping/runtime plugin requests proposed; patch coverage and legal limits remain unverified.
- **Distribution decision.** [CDPR fan-content guidelines](https://www.cdprojektred.com/en/fan-content), [REDmod EULA](https://cdn-l-cyberpunk.cdprojektred.com/redmod_eula_en.pdf), accessed 2026-10-28: game data/art are not automatically open-source because extraction code is. Owner must settle allowed distribution and permissions. Initial route is a local user-supplied pack; a future configured online provider belongs in Core with provenance/cache policy, never inside the plugin.

### Integrity / crypto

- No further key is required for the **observed decrypted CSAV container**. [CyberCAT compression](https://github.com/WolvenKit/CyberCAT/blob/50db3263a94247531853ba13c2e200f2e784ba8d75/CyberCAT.Core/Classes/CompressionHelper.cs) and [WolvenKit save I/O](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/IO) describe structural repair without a container checksum/signature. PC framing agrees with the sample. This does not prove no checks exist inside opaque nodes, user.gls or console wrappers. No credentials/keys were read.
- TweakDBID uses CRC32/IEEE plus string byte length; facts use FNV-1a/32; forced-stat names use FNV-1a/64. These are identity hashes, not CSAV integrity checks. Independent item and exact fact-name checks above verify their joins; preserve full IDs and fact whitespace.
- Existing repairs cover property/package lengths and offsets, containing node size, later virtual offsets, chunk sizes/file offsets and footer pointer. New item/node creation additionally needs IDs, sibling/child/next links, inventory counts, instance counters and references. Omitted-property insertion is not proof that all structural mutations work.
- [LZ4 specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md), BSD-2-Clause source project, and [K4os](https://github.com/MiloszKrajewski/K4os.Compression.LZ4), MIT, support retaining the managed codec. Every sample chunk expanded to its declared length in the independent scratch decoder. No codec replacement is needed. Console restore/signing remains the owner's external save-manager workflow.

## Vision

- **Overview:** game/schema/DLC evidence, character summary, supplied screenshot, capability reasons and change summary. Distinguish hash-confirmed identity, inferred gameplay meaning and console-tested writes.
- **Character:** attributes, level/Street Cred/skills, point budgets and post-2.0 perk/relic ranks, descriptions/prerequisites. Keep current free point setters separate from a budget-preserving build transaction; versioned presets show a diff before applying.
- **Inventory/equipment:** searchable categorized player/stash views, names/icons/descriptions, raw record and full instance IDs, quantity/tier/modifiers, quest/equipped state. Details explain unsupported fields. Selected refill actions affect only supported existing stacks with evidenced caps. Cyberware slots show owned compatible choices and capacity contributors before writes.
- **World:** garage, travel list/eventually map, journal/quest/collectible dashboards with confirmed/inferred/unknown states. Pins disclose source record/coordinates. Story diagnostics precede any narrow validated future-event preset; no universal complete-all/reset-quest action.
- **Advanced:** version-aware named facts, raw node diagnostics and semantic/binary diff. Unknown data remains inspectable without invented labels or arbitrary writes.

## Phased plan

### Phase 1 — name existing data (M)

Goal: immediate read-only meaning without a new gameplay writer.

- [ ] Freeze baseline/corpus manifest and retain current unsupported-version behavior.
- [ ] Add exact fact-name lookup, whitespace vectors, collision handling and raw fallback.
- [ ] Expand attributed trimmed item/vehicle/travel identifiers; expose catalogue version/coverage.
- [ ] Define normalized reference-pack schema and licensing manifest; enrich existing records with available text.
- [ ] Keep lazy text views and missing-image placeholders until data/UI support lands.

Evidence: baseline hashes and source manifests, R2/R8. Risk low for reads, medium for misleading labels. A named fact is not automatically quest completion.

### Phase 2 — inventory/equipment/cyberware inspection (L)

Goal: explain existing item instances and ownership.

- [ ] Decode extended items read-only; preserve opaque suffixes and join full instance identity to stat modifiers.
- [ ] Follow owner-1 loadout handles to inventory instances, not row positions/base ID alone; separate stash/other containers.
- [ ] Inspect installed versus owned cyberware, slot compatibility and capacity contributors, explicitly showing unknown sources.
- [ ] Add rich cells/details, category/container/equipped filters and whole-list search (R4/R7/R9).
- [ ] Import user-produced locale/icon packs outside plugins through immutable context (R6).

Evidence: paired equip/stash/upgrade/cyberware saves and same-build TweakDB, R1/R2. Risk high schema ambiguity; writes remain disabled. Text equipment tables can ship before graphical slots.

### Phase 3 — validated existing-value actions (M–L)

Goal: improve supported edits without creating inventory instances.

- [ ] Verify real material/ammo caps and 2.x healing/grenade charges; replace unsupported cap claims.
- [ ] Add selected supported-stack refill/set-target actions with atomic prevalidation and one undo group.
- [ ] Validate extended flags/modifier fields individually; enable only proven effects with resolved dependencies.
- [ ] Preview calculated changes/exclusions and retain unknown bits/nonselected values (R10).
- [ ] Console-test baseline money/attribute/point edits and new actions (R5).

Evidence: stack/charge differential pairs and load/use/re-save observations. Risk medium–high semantics; start with quantities already writable. Existing ActionNode/ValueChanges suffice for fixed-value bulk actions.

### Phase 4 — progression and build management (L)

Goal: consistent level transitions and post-2.0 perk allocation.

- [ ] Read allocated perks/relic ranks, skills, spent/unspent budgets and dependent stats/effect handles.
- [ ] Obtain same-build XP curves, caps/rewards/unlocks and test boundary pairs.
- [ ] Enable XP/level transitions only after dependencies are proven; retain read-only presentation otherwise.
- [ ] Implement budget-preserving allocation/refund, prerequisite checks and coherent undoable mutation.
- [ ] Add version/DLC-aware build presets and before/after diff; never reuse pre-2.0 perk enums blindly.

Evidence: R11 transition/rank/refund pairs, R2 rules, R3/R12 transaction tools. Risk high; current point/attribute setters alone are not a full build editor.

### Phase 5 — world diagnostics then narrow actions (L)

Goal: named garage/travel/journal/collectible views.

- [ ] Decode journal/mappin/unlocked-location records read-only; distinguish journal hashes, quest paths, fact names and marker refs.
- [ ] Inspect garage persistency/travel handles; distinguish ownership, unlock and current availability.
- [ ] Show evidence per quest/collectible state; add maps only after coordinates/assets are available (R13/R14).
- [ ] Validate an existing vehicle/travel flag against differential saves before offering a specific action.
- [ ] Keep facts read-only by default; any future story preset requires explicit stage prerequisites, all dependent changes and successful forward play.

Evidence: R15 car/travel/quest/collectible pairs. Risk very high for story writes; read-only diagnostics are independently useful. No profile/global writes planned.

### Phase 6 — structural inventory and more versions (L)

Goal: safe stack insertion/removal, then assess unique gear and equipment mutation.

- [ ] Add snapshot/command undo with binding/reference restoration and stable instance identities (R3/R16).
- [ ] Prove node creation, duplicate prefixes, counts/links/IDs, unique counters and dependent stat references.
- [ ] Start with vanilla quantity-only stack split/merge/add/remove; prohibit equipped/quest/unique/unknown deletion.
- [ ] Defer unique gear generation until seeds/mod slots/stats/entitlement initialization are known; repair and independently reparse only affected regions.
- [ ] Add schema profiles individually, including 277/2500 only after PS5 evidence; never widen the 2.21 guard on PC feature claims.

Evidence: structural differential saves and PS5 restore/re-save, R1/R5/R17. Highest risk. Appearance/body transfer is a separate future project requiring morph/resource/voice/cyberarm evidence.

## Requirements

Blocking refers to the named capability, not all useful progress. Framework/UI changes are separate maintainer work; plugin implementation must retain its folder boundary.

| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
| --- | --- | --- | --- | --- | --- |
| CP2077-R1 | Samples | PS5 2.21 pairs for buying/using one stack, equip/unequip, stash move, upgrade once and install/remove cyberware; record build/DLC/lifepath and observations. | P2–3 ownership, instance schema and dependencies. | Yes for new writes | Current guarded setters and read-only views. |
| CP2077-R2 | Data | Same-build TweakDB/localization pack: full IDs, names, categories, descriptions, DLC, perk ranks/prerequisites, slot rules and progression curves; manifest source hashes/coverage. | P1–5 meaningful data/rules; generated classes lack actual values. | Yes for full semantics | Internal identifiers and Unknown labels. |
| CP2077-R3 | SDK | Reversible command or document-snapshot undo/redo restoring model, bindings and references for structural changes. | P4 new allocations/P6 item creation; ValueChange cannot express deleted records. | Yes for structural undo | Defer operations; fixed-field actions remain undoable. |
| CP2077-R4 | UI | Rich virtualized cells/details rendering images/actions, confidence/warnings, accessible icon labels, filters and selection. | P2; TableView currently blanks non-text/non-field cells. | Yes for rich UX | Text tables plus separate image sections. |
| CP2077-R5 | Human | Owner tests spare-slot restore, load/effect/use/equip or quest advance, re-save/reload and unrelated state; log results by build. | Every new write; human alone promotes Stable. | Yes for game acceptance | Experimental with exact tested scope. |
| CP2077-R6 | Core | Bounded reference/asset pack import and immutable per-document context exposed through SDK; manifest build/locale/DLC/licence/hash and deterministic missing-pack fallback. | P2 data/icons; GetAsset serves bytes but cannot ingest user packs. | Yes for local full packs | Embed reviewed small resources; placeholders. |
| CP2077-R7 | SDK | Reference-value spec/provider with exact string IDs, searchable/paged candidates and display/asset metadata. | P2/P6 scalable picker; EnumSpec embeds every option. | At catalogue scale | Small enums/text IDs with plugin validation. |
| CP2077-R8 | Data | Source/licence ledger and distribution decision for text/icons/catalogues; canonical fact strings separate from display labels; detect collisions. | P1 onward; MIT code does not license all game data. | Yes for distribution | Current attributed names and placeholders. |
| CP2077-R9 | SDK | Optional bounded table query/page contract, stable row keys and whole-list filter/search. | P2 inventory/facts search; current frontend filters supplied page only. | For unified large lists | Existing 128-row lazy pages or search action. |
| CP2077-R10 | UI | Calculated action preview of affected/excluded records, old/new/derived values and warnings; one undo group. | P3 refill/P4 presets; current confirmation is static. | For preview UX | Preview section plus confirmed action. |
| CP2077-R11 | Samples | Before/after level/Street Cred threshold, skill reward, perk-rank buy/refund and Relic access; base and PL entitlement states. | P4 rewards/prerequisites/budgets. | Yes for progression writes | Existing read-only level/XP and separate point setters. |
| CP2077-R12 | Tooling | Offline decompressed-node/RED-property differential analyzer, identity joins, synthetic fixtures and independent expected-byte assertions. | P2–6 prevents offset guesses and self-confirming round trips. | For new schema validation | Current scratch Python and manual diffs. |
| CP2077-R13 | SDK | Optional location/map node with coordinate-transform metadata, stable pin IDs/status and linked details. | P5 graphical travel/collectible map. | Only for map | Named location tables. |
| CP2077-R14 | Data | Versioned journal/quest/mappin catalogue, state joins, names/coordinates and approved map/icons; transform landmarks. | P5 reliable completion/location mapping. | Yes for map/completion | Raw hashes/named facts; no invented completion %. |
| CP2077-R15 | Samples | Before/after car acquisition/summon, travel discovery, tarot/PL collection and quest phase/gig; precise stage observations. | P5 separates facts/journal/persistency/mappins. | Yes for world writes | Read-only diagnostics. |
| CP2077-R16 | SDK | Identity/reference metadata linking instances, slots and derived fields; removed-binding invalidation and stable identity through rebuilds. | P2 navigation/P4/P6 structural safety. | For structural UX | Plugin-owned stable IDs and textual references. |
| CP2077-R17 | Samples | Later/current-build PS5 saves, 277/2500 if obtainable, mature inventory, DLC/non-DLC and other regions; matched PC transfer optional. | P6 wider version scope. | Yes to widen support | Retain exact version/schema guards. |
| CP2077-R18 | Human | Choose reference-data distribution and GPL adaptation policy; authorize lawful local export and obtain asset permissions where needed. | P1–2 availability/licensing. | Yes for bundling/port choices | Existing permissive references; no art bundle. |
| CP2077-R19 | Core | Semantic export validation/report for intended values/reference resolution and unrelated-value preservation; cover final ZIP/backup mirroring. | P3–6 re-open alone misses semantic errors. | For shared report | Plugin Serialize independently verifies semantics. |
| CP2077-R20 | UI | Per-feature support/reason/source/build/DLC labels and policy-limit versus game-rule distinction. | All phases; plugin Experimental alone is too coarse. | No | TextNode/FieldNode warnings and README matrix. |
| CP2077-R21 | Core | Candidate-model batch transactions with final-state invariants and complete derived/structural rollback; coordinate conflicting sibling outputs if metadata coupling proves necessary. | P4/P6 coherent build/equipment operations; multi-file output already exists. | For complex transactions | Plugin-local candidate clone and existing outcomes. |
| CP2077-R22 | UI | Accessible perk graph with ranks/prerequisites/budget and build diff; equipment/cyberware slot selector with compatibility/capacity reasons. | P4 finished character builder. | Only graphical UX | Grouped sections/tables/actions. |
| CP2077-R23 | Tooling | Per-capability corpus manifest, independent hashes/patches, no-op/undo/corruption/missing-sample tests and bounded-view measurements. | Every release gate. | Yes for releases | Extend current conformance/format tests later. |

## Acceptance criteria

- [ ] Plugin implementation stays in `plugins/Cyberpunk2077/`; framework/UI work is separate. Use composition and small maintainable files under the stricter applicable limits; add no plugin hooks/build targets.
- [ ] Every supported build/capability has evidence and refusal behavior; unsupported versions retain safe inspection/original bytes without automatic conversion.
- [ ] Baseline 22 item identities remain resolved; unknowns retain full hashes. All researched exact fact strings hash correctly, including whitespace regressions; ambiguous names never silently win.
- [ ] Data packs declare version/DLC/locale/provenance/licence/coverage. Missing or stale packs fall back to identifiers/placeholders without breaking existing edits. Plugins perform no network/filesystem I/O.
- [ ] Equipment joins use full instance identity and verified ownership; duplicate base records remain distinct. Perk/cyberware invariants have source evidence and differential tests.
- [ ] Limits distinguish storage, editor policy and verified game rules. Unsupported XP/perk transitions remain read-only until rewards/unlocks/dependent effects are understood.
- [ ] No-op and edit→undo serialization stay byte-identical for every sample. Preserve unrelated payload bytes, opaque bodies and untouched compressed chunks except necessary offset/length repairs; verify existing Core backup mirroring separately.
- [ ] Edited output independently reparses all intended values and derived changes. Repair actual structural/integrity dependencies; reject corrupt ranges/counts/handles/duplicate identities and oversized resources safely.
- [ ] Bulk actions validate atomically, target supported selected records, preview exclusions/effects and undo in one step. Structural operations remain disabled until identity/binding/reference restoration is proven.
- [ ] Conformance and meaningful format/edit tests pass with MTP `--project`; absent private samples skip. These checks belong to implementation, not this research task.
- [ ] Large views stay bounded/lazy and search scope is explicit. Measure against the existing roughly two-second opening target on documented hardware; eventual query support avoids whole-catalogue payloads.
- [ ] Quest diagnostics never equate named/debug/absent facts or metadata summaries with full completion. Story changes need stage-specific consistency evidence and successful forward play.
- [ ] Every new write category has recorded PS5 load/effect/re-save/reload evidence. Parse success and identical backup twins do not count; only a human sets Stable.
- [ ] README lists pinned sources, notices, rights route, verified/inferred/unknown mappings and remaining blockers. No private saves, extracted game data or credentials enter the repository.

## Open questions & risks

- One early-game 2.21 snapshot does not establish mature/DLC extended inventory, perk, garage or persistency coverage. New 2.5 PC/macOS tooling is research evidence, not supported PS5 scope.
- Which cyberware capacity contributors persist, and which are recomputed? Does perk removal require effect package deletion, stats recalculation or callbacks? Generated property names alone cannot answer this.
- Are metadata level/quest summaries required to match gameplay for display/cross progression? Current files belong to GenericJson; any future coupling needs differential evidence and coordinated ownership.
- Fact storage is uint32 while scripting documentation describes signed values. Establish per-fact signed display/ranges; never convert everything to bool. Canonical whitespace and 32-bit collisions matter.
- Facts may be debug tickets or future conditions. Active quest phases, journal state, callbacks and mappins can disagree even when the game loads; no generic reversal promise.
- TweakDB is base data; seeds, unique counters, modifiers, attachments and entitlement determine actual instances. A base-ID swap is not validated item generation or upgrading.
- Console restore/signing, region restrictions and cloud reconciliation lie outside CSAV editing. Owner supplies a working restore path; no credential solicitation or undocumented cloud service integration.
- Source availability is not asset redistribution permission. Useful identifier views can ship while richer packs await data/rights decisions.
- No container checksum has been observed; opaque-node/profile integrity remains Unknown. Preserve it and require game tests instead of inventing a checksum or asserting none exists anywhere.
- Structural undo, reference context and query support need shared-team work. Current tables/actions are viable early-phase workarounds, not the finished inventory/build editor.
