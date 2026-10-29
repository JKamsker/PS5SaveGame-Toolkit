# TalesOfArise — improvement plan

> **Historical planning snapshot — 2026-10-28.** The baseline, proposed work, test counts and
> policy restrictions below describe that research round, not current implementation or permissions.
> See [current plugin status](../PLUGINS.md), the linked plugin README,
> [delivered framework features](ROADMAP.md) and [current policy](../PLUGIN_GUIDE.md#hard-rules).

## Summary

The `tales-of-arise` plugin already decrypts, validates and rewrites supplied PPSA02745 base-game slots while preserving unknown data; curated editing covers gald, six characters' level/HP/SP and 13 consumable IDs. A substantially better editor would provide named inventory and equipment, complete character sheets, artes and skill panels, and a completion tracker for owls, quests, cooking, fishing and NG+ artifacts. The three biggest blockers are **controlled PS5 differential saves**, **a licensed, versioned ID-to-game-data catalogue**, and **human console validation of edits and dependent state**. Existing framework primitives can deliver an initial read-only expansion and conservative scalar edits; rich pickers, locally supplied data packs and reversible structural operations require specific framework/UI work. Base-game crypto is not the current blocker, and an editable Grade currency must not be invented from internal class names.

## Baseline state (historical evidence)

- Inspected **2026-10-28**, HEAD `885b2f8eec6ecbb9353794c3623b50ec45343e41`. Read AGENTS, plugin guide/status/sample/format documentation, plugin source/tests, SDK, Core sessions/export, web renderers and [issue #1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1) through `gh issue view 1`. This run made no repository edits, builds, .NET tests, commits or external posts.
- `plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/TalesOfArisePlugin.cs`: supports PPSA02745 gameplay names, exact 760,856-byte size and PS5 mode, with Experimental maturity. Other regions/platforms, Beyond the Dawn and the opaque 4,096-byte `SystemSaveData.sav` are unsupported.
- `plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/{AriseFormat,AriseCipher}.cs`: stored ciphertext SHA1, AES/XOR, evolving key/feedback, shuffled linked directory and 30 strict section schemas are implemented. Bounds/alignment/overlap/cycles and unsupported versions are rejected. Serialization re-encrypts/reparses and compares plaintext; outer bytes `0..0x32057` remain verbatim.
- `plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/AriseDocument.cs:10`: body-relative fields are PartyProfile gald `+0x18`, play seconds `+0xAF8`, ticks `+0x2000`; character level `+4`, HP `+8`, XP `+0x10`, SP `+0x30`, base HP `+0x50`. XP/base HP/time are read-only. Level editing does not update XP, derived stats or the load-screen preview. Gameplay meanings remain **Inferred**, even where byte mechanics are verified.
- `AriseDocument.cs:127`: scans **934 × 16-byte** common inventory records: quantity byte 0, flags byte 1, uint16 ID at +2. Only existing IDs 1–13 are writable, capped at 15; zero IDs are omitted. Unknown rows retain numeric IDs. Remaining ItemManager regions, unique accessory instances, equipment references, acquisition flags and insertion/removal are not decoded.
- `plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/Resources/{Characters,Consumables,Sections}.csv` embeds names for characters/13 consumables and measured section schemas. `AriseExplorer.cs` exposes all 30 sections / 138,832 raw uint32 words in lazy pages of at most 256 words.
- `plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests/{AriseSampleTests,AriseCipherTests,AriseCorruptionTests,AriseExplorerTests,TalesOfAriseConformanceTests}.cs` cover independent cipher/hash vectors, known bytes, malformed chains, no-op/edit/reopen/undo preservation and bounded exploration. README reports 39 plugin and 43 Core tests passing previously; those suites were **inspected, not rerun** under this brief.
- **Reverified this run:** read and reused the locally authored scratch `inspect.py`, then ran it with `python -I`. All six gameplay ZIP records match stored SHA1, validate 30 section IDs/versions/sizes and decrypt→encrypt byte-identically; every primary equals its backup. These are **three distinct early-game slots**, not six independent experiments: seconds 1,890/2,770/3,166, gald zero, six levels all 5, and 24/24/25 nonzero inventory IDs. The reused sample-verification report identifies slot 002 record 24 as item ID 1, quantity 1, flags `0x41`; this supports the existing Apple Gel inference, not acquisition behavior or upper bounds.
- **New negative evidence:** own `additional_evidence.py` reused the decoder read-only, writing `scratch/TalesOfArise/additional-byte-evidence.json`. Applying the PC table's apparent `-0x40` prefix adjustment to Attack through Resistance lands at PS5 body `0x3C..0x4C`, all zero for all six characters/all slots; these are not established effective-stat mappings. PS4 owl-counter candidate PartyProfile body `0xB64` is zero throughout, while SearchOwl differs at 61 bytes between slots 000 and 001. Nonzero bytes/changed sections cannot be interpreted as a collectible count. No new gameplay meaning was promoted to Verified.
- Existing framework: `src/SaveToolkit.Sdk/Views/ViewNode.cs` has tabs, lazy sections, scalar fields, tables, images, actions and hex; `Values/FieldSpec.cs` has enum descriptions/static ranges. `ISaveDocument.cs` already supports multiple FileOutputs and StructureChanged. BoundSaveDocument supports scalar rollback; Core DocumentSession supports scalar history; WorkspaceExporter reparses output, detects conflicting writers and mirrors originally identical backups. Basic undo, multi-file output, table search and standalone assets are **not missing**.
- Actual UI gaps: `web/src/components/document/TableView.tsx` renders only field/text cells although TableRow accepts arbitrary ViewNodes, so image/action cells are blank. `FieldControl.tsx` uses native selects; there is no searchable reference picker. Table cells omit FieldRow's visible confidence treatment and mainly expose warnings via tooltip. `ViewRenderer.tsx` has standalone images but no map or skill-graph node. Core indexes asset IDs from ImageNodes, while the plugin-open contract has no separate reference-data context.

## Research findings

### Existing editors/tools (same game, other platforms)

- **Best format reference:** [save-converters Arise README](https://github.com/ItsAllAboutTheCode/save-converters/blob/6514103c035d5f9719fd871ba2ca447ddc310c33/save_convert/tales_of/arise/README.md), [notes](https://github.com/ItsAllAboutTheCode/save-converters/blob/6514103c035d5f9719fd871ba2ca447ddc310c33/save_convert/tales_of/arise/save_NOTES.md), [implementation](https://github.com/ItsAllAboutTheCode/save-converters/blob/6514103c035d5f9719fd871ba2ca447ddc310c33/save_convert/tales_of/arise/tales_of_arise_save_converter.py) and [base/DLC fixtures](https://github.com/ItsAllAboutTheCode/save-converters/tree/6514103c035d5f9719fd871ba2ca447ddc310c33/tests/test_files/arise), SHA `6514103c035d5f9719fd871ba2ca447ddc310c33`, checked 2026-10-28. Explicit PC/PS4/PS5 decryption/encryption/conversion; Xbox support is explicitly untested. **GPL-3.0**: copying/adapting implementation requires a compatible licensing decision, not a permissive assumption. PS5 base-container transfer confidence **high**, verified on our bytes; expansion/PC conversion **not verified here**. Cached converter Git blob matches this revision; code was read, not run.
- **PS4 patch research:** [main thread](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070), [ロード attachment 31135.txt, 2022-04-03](https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt), [item/character discussion](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30162). Provides section-ID anchors, relative gald/XP/SP/base-HP offsets, inventory count/stride and skill/play-record leads. Subtract **0xC** from offsets relative to the ID at header +4 to obtain body offsets. Licence **not established**: independently verify facts, do not redistribute patches. Transfer **medium** for current tested anchors, low for new flags/unlocks. Existing records agree with the current mapping; owl candidate is zero in all samples. Reused/downloaded text was decoded from percent-encoded CP932; live pages timed out/were inaccessible, so forum provenance partly inherits the earlier research.
- **Specific inventory regression lead:** that attachment reports accessory-material/chest and key-item acquisition failures after indiscriminate 99-stack changes. It distinguishes previously acquired items from forcibly inserting every item. Use this as a regression scenario, not as a bulk-action design. No PS5 reproduction was performed.
- **PC runtime mappings:** [Sora3100 Arise.CT](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/c92871084d714d98b3854ba9fbb4e3f022145d7e/Arise.CT), [README](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/c92871084d714d98b3854ba9fbb4e3f022145d7e/README.md), SHA `c92871084d714d98b3854ba9fbb4e3f022145d7e`. README targets the 2023-11-09 PC build. Covers party, stats, proficiency, Devil's Arms kills, titles and artes; XML includes **482 arte-ID dropdown lines including Dummy**, plus named character title/arte entries. Memory pointers and `0x14` arte / `0x38` title strides are not proven disk structures. No licence found (GitHub licence metadata null): obtain permission before copying code/tables. Transfer **medium** for established prefix fields, **low/unverified** elsewhere; uniform offset conversion failed the stat check above. Cached CT matches pinned blob; parsed XML only, never ran scripts.
- **PC Gald Editor:** [Nexus mod 407](https://www.nexusmods.com/talesofarise/mods/407), EnvyOfMen, 2026-02-13. Instructions attach Cheat Engine to Steam: this is a runtime table, not a save parser. Permissions forbid redistribution and require modification/asset permission. Transfer **low**, no sample verification or code reuse proposed.
- **PS4 commercial tools:** [Save Wizard FAQ](https://www.savewizard.net/faqs/) excludes native PS5 saves; [XDG quick-code index](https://old.xdgmods.com/category/save-wizard-quick-codes/) lists Arise (2021-09-26). Proprietary/uncleared code is a feature lead, not a redistributable parser. No independent PS5 verification. The [official 2021-12-13 transfer announcement](https://en.bandainamcoent.eu/tales-of/news/tales-of-arise-new-patch-and-save-data-transfer) confirms in-game PS4→PS5 and Xbox One→Series migration; that does not establish binary identity or arbitrary platform conversion.
- **Search coverage/limits, 2026-10-28:** searched GitHub/GitLab, Nexus, Cheat Engine/FearlessRevolution, Apollo/Save Wizard, save-editor forums, Reddit/GBAtemp/PSX-Place/se7ensins and 010-template terms. No separately verified full Arise PS5 editor, Xbox fixture or 010 save grammar was established beyond these leads. Some forums were blocked; this is a bounded negative search. The [publisher announced Switch 2 Beyond the Dawn Edition for 2026-05-22](https://en.bandainamcoent.eu/tales-of/news/tales-of-arise-comes-nintendo-switch-2-22-may-2026); no Switch 2 parser/sample was verified. Older Switch Tales editors do not establish Arise support.

### Same engine / same studio

- **Arise-SDK:** [emoose repository](https://github.com/emoose/Arise-SDK/tree/3efa5d41fa6d86d066d6db0f3d1afef9b7146a60), SHA `3efa5d41fa6d86d066d6db0f3d1afef9b7146a60` (2024-02-24), **GPL-3.0**. Cached [classes](https://github.com/emoose/Arise-SDK/blob/3efa5d41fa6d86d066d6db0f3d1afef9b7146a60/src/SDK/Arise_classes.h) and [structs](https://github.com/emoose/Arise-SDK/blob/3efa5d41fa6d86d066d6db0f3d1afef9b7146a60/src/SDK/Arise_structs.h) match pinned Git blobs. UAriseItem separates quantity/get/new/visual flags and unique work data; FItemWorkData carries instance identity, ItemID, parameters, abilities, rank and flags. Accessories therefore need more than a common stack model. PC runtime definitions only; PS5 serialization transfer **low/unverified**. Do not run the DLL or copy GPL/generated headers into the plugin without the appropriate licensing route.
- **Asset extraction:** [Spiritovod's Arise UModel thread](https://www.gildor.org/smf/index.php?topic=7906.0), 2021-09-10 onward, documents **UE4.20**, textures/models and custom-material caveats. [UEViewer](https://github.com/gildor2/UEViewer/tree/a0bfb468d42be831b126632fd8a0ae6b3614f981) is MIT; forum-modified binaries need separate provenance review. [FModel](https://github.com/4sval/FModel/tree/05cfbdd1648afe2928109b70ab63880a4bc8f7b4), GPL-3.0, [CUE4Parse](https://github.com/FabianFG/CUE4Parse/tree/808a7d65aea71cf3d58e142692b4f14215b7c79a), Apache-2.0, and [UAssetAPI](https://github.com/atenfyr/UAssetAPI/tree/b4e8a012939295a6fd890ddc64e57ea78b709b95), MIT, are offline table/texture extraction candidates. Keep them external reviewed tooling initially. Code licences do not license extracted game content. No installation assets were supplied/extracted, so table/icon recovery and PS5 joins remain **unverified**.
- **Generic Unreal saves:** [trumank/uesave](https://github.com/trumank/uesave/tree/1917a22ab69e9a4045e613d9d7f39a49fe00bd46), MIT, handles GVAS. Arise's measured encrypted linked sections are custom; generic GVAS support does not provide its grammar. Transfer **low/no direct reuse established**, no sample tool run. Share appropriate asset tooling, not an assumed save serializer.
- **Sibling Tales:** [Berseria decrypter](https://github.com/bucanero/save-decrypters/tree/d446481e6eff876d61be4a0d717995d08d70c8f2/berseria-decrypter), GPL-3.0, documents PS3/PS4/PC decryption/custom SHA1; [Graces remastered converter](https://github.com/ItsAllAboutTheCode/save-converters/blob/6514103c035d5f9719fd871ba2ca447ddc310c33/save_convert/tales_of/graces/README.md), GPL-3.0, documents multi-platform/YAML editing. Useful workflow/terminology comparisons only. Shared Arise keys, layouts or hash coverage are **not established**, with no PS5 comparison. No sibling code port proposed.

### Game data sources (names, descriptions, icons, limits, quests)

- **Best full-ID route: user-owned game tables.** SDK FItemData exposes ID, UniqueId, label, category, icon reference, MaxNum, DLC identifiers and parameters. UItemLibrary exposes ID/label/unique-ID conversion, name/explanation and picture ID; GetItemMaxNum suggests context-dependent limits. Project settings identify ItemDatabase/Dictionary, QuestDatabase/Dictionary, PlayerStatus, CookingRecipe, CollectionFish, Artifact, ScenarioBitFlag and MapIcon assets. Formats are Unreal/custom database assets and localized dictionaries; normalize to JSON/CSV with optional images. Coverage is potentially broad but **not yet obtained**. Resolve which database ID namespace matches save uint16 IDs using known consumables and observed equipment. SDK code is GPL; tables/text/icons are game-company content: prefer local user-owned dumps and separately approved factual mappings, not default redistribution of descriptions/icons.
- **Collectible joins:** FSearchOwlData has numeric ID, actorId, OwlPointID/SubOwlPointID, region, item label and model. This is an extraction lead for actor/label→location/reward, not an obtained map or proven save index. Fishing/cooking/quest types offer similar leads. Require source build/language and namespaces; no PS5 join verified.
- **Aselia:** [ToA data category](https://aselia.fandom.com/wiki/Category:ToA_Data), [rifles](https://aselia.fandom.com/wiki/ToA_-_Weapons_%28Rifles%29), [books](https://aselia.fandom.com/wiki/ToA_-_Weapons_%28Books%29), [consumables](https://aselia.fandom.com/wiki/ToA_-_Consumables), inspected/search-indexed 2026-10-28. HTML/MediaWiki-style coverage includes localized names, effects, equipment stats/categories, recipes, skills and artifacts; no verified save-ID keys. [Fandom policy](https://www.fandom.com/licensing) defaults text to CC BY-SA 3.0 with per-wiki exceptions: verify actual licence and preserve source/revision/attribution before distributing adapted text. Media licences are separate; game icons do not inherit text licensing. Some pages were robots-blocked; no complete API/export was tested. Names are cross-checkable, automatic PS5 joins **unverified**.
- **Completion data:** original-author [PowerPyx owls](https://www.powerpyx.com/tales-of-arise-all-owl-locations/), [fish](https://www.powerpyx.com/tales-of-arise-all-fish-locations/), [subquests](https://www.powerpyx.com/tales-of-arise-all-sub-quest-locations-side-quests/) and [hub](https://www.powerpyx.com/tales-of-arise-wiki-strategy-guide/), 2021-09-08. HTML/screenshots cover 38 owls, 44 fish/12 locations, 70 base subquests, with recipes/artifacts/flowers linked. Guide-order numbers are **not save IDs**. No permissive licence established: link and independently cross-check facts or request permission; do not bundle prose/screenshots/maps. No sample join verified.
- **Other catalogues:** [Samurai Gamers index](https://samurai-gamers.com/tales-of-arise/), [TrueAchievements items](https://www.trueachievements.com/game/Tales-of-Arise/walkthrough/21), [Neoseeker crafting](https://www.neoseeker.com/tales-of-arise/Crafting/Weapons), accessed 2026-10-28. HTML supplies named categories/effects, recipe ingredients, prices and equipment restrictions, not confirmed save IDs or a licensed icon dump. Copied text/image rights are **uncleared**; use links and independent checking. Audit DLC/localization coverage. Never assign IDs by list position.
- **Publisher limit evidence:** [Bandai Namco DLC catalogue](https://toarise.tales-ch.jp/flg/products/dlc.html) and [Relief Support Pack description](https://www.xbox.com/en-US/games/store/tales-of-arise-relief-support-pack/9PDZTWQKK7J5) explicitly describe 99-consumable capacity and **9,999 HP maximum**; publisher catalogue also states level 100. This challenges the current 99,999 HP guardrail. HTML facts, not ID tables; PS5 state-dependent behavior remains untested. Plan validated 15/99 stack rules and distinguish base/current/effective HP; preserve unfamiliar originals read-only rather than silently clamping. Do not edit DLC ownership to obtain capacity.
- **Grade correction:** original-author [AzMuch NG+ guide, 2022-07-28](https://gamefaqs.gamespot.com/ps4/265989-tales-of-arise/faqs/80006/new-game-plus) and [RPG Site, 2021-09-11](https://www.rpgsite.net/feature/11724-tales-of-arise-new-game-plus-and-how-it-works-and-what-carries-over) describe artifact-based carry-over instead of spending Grade. SDK retains GradeShop/GetGrade/SetGrade names, which do not prove a currency. Show NG+ artifact ownership/effects; alleged grade/score stays Unknown until its gameplay role is demonstrated. Guide prose is not licensed for wholesale embedding; no grade offset verified.
- **Catalogue needed:** manifest with game/build/schema/language, source revision and per-field rights; rows with save ID, database ID/label, category, name, optional description/icon, base/conditional cap, equip restrictions and prerequisites. Track missing/inferred/byte-matched/human-confirmed joins. Rights-cleared factual CSV/JSON can be embedded today. Private game dumps need Core-owned local import/assets because plugins cannot do filesystem/network I/O. Any future runtime fetch must be explicit, user-configured and Core-owned with caching/source terms.

### Integrity / crypto

- **Verified base schema:** public material already in AriseCipher is sufficient; no user secret required. Stored SHA1 at absolute `0x32060` covers final physical ciphertext `0x32078..EOF`; mode `03 01` is at **0x32074**, correcting upstream prose `0x32068`. Pointers are relative to `0x32058`; PS5 directory pointer is at decrypted absolute `0x32090`. Length includes the 0x20-byte prefix. Scratch verification reproduced all six inputs; this is byte compatibility, not console acceptance.
- AES uses the first 32 key bytes; 64-byte feedback persists across regions with adjacent-byte swapping, and per-region plaintext SHA1 evolves the key. One edit can change subsequent ciphertext broadly: compare preservation in **decrypted section-relative state**, plus outer bytes. Keep the existing implementation; pinned converter is research, not code approved for copying.
- **Unknown:** section-name hashing, outer preview semantics/integrity, SystemSave decoding, expansion/version schemas and alternate selectors. Section IDs remain stable while bodies vary, so they are not established body checksums. No evidence currently requires another base-game signature/signing key. Research preview/SystemSave separately; do not apply gameplay SHA1 or AES blindly.

## Vision

- **Overview:** slot/build/schema, integrity, playtime, active party, gald, current/max CP and clearly separated gameplay/derived/preview information.
- **Characters:** named sheets with level/XP, current/base/effective HP, SP costs, validated stats, equipment/accessory effects, artes/use/proficiency and skill-panel prerequisites/bonuses. Unknown guest/DLC slots remain explicit.
- **Inventory/equipment:** searchable named categories, descriptions/icons, quantity/effective cap, owned gear and accessory instances; compatible equipment comparison/pickers. Preserve numeric unknown IDs/flags. Creation/removal follows proven allocation/acquisition invariants.
- **World/completion:** quests, owls found/reported/rewards, recipes learned/cooked, fishing notes/rewards, artifacts/NG+, camps/maps and skits. First provide read-only location checklists; maps require validated coordinates and asset rights.
- **Actions:** refill selected existing consumables, restore supported HP/CP and apply validated build presets with semantic preview, dependency checks and grouped undo. Exclude unknown/key/accessory/story rows from generic refills; avoid indiscriminate completion flags.

## Phased plan

### Phase 1 — evidence and catalogue foundation (M)

Goal: remove uncertain assumptions before expanding writes.

- [ ] Create an evidence ledger: section ID/version/body offset/width, source revision, observations, confidence, dependencies and next experiment; separate gameplay, preview and system data.
- [ ] Collect R1/R12–R15 sample pairs with control re-saves; pin cached source/licence manifests and fingerprint downloads.
- [ ] Build section-aware diff tooling and normalize one user-owned item/dictionary extract; establish save ID↔ID/UniqueId/label with observed items.
- [ ] Audit HP/stack guardrails against publisher facts; preserve unsupported originals read-only. Do not describe level-only edits as coherent growth recalculation.

Evidence: labelled saves/menu observations, game tables, data-rights decisions. Risk: early-game snapshots give misleading agreement. Exit: build/ID mismatches fail clearly; numerical coincidence alone never upgrades confidence.

### Phase 2 — named read-only editor (M–L)

Goal: useful inventory/character/progress information before complex writes.

- [ ] Add typed lossless readers for common inventory, candidate equipped references, character stats/proficiency and separate accessory work data.
- [ ] Resolve names/categories/limits with numeric fallback/provenance; show all nonzero inventory IDs and distinguish preinitialized characters from recruited members.
- [ ] Inspect PartyProfile, SearchOwl, QuestSaveDataEx, FishingSaveData, CampPoint and scenario records with bounded section-specific readers.
- [ ] Add demonstrated artifact/recipe/fish/owl/quest read-only views, joining authoritative labels rather than guide order; use current lazy tables while richer UI develops.

Evidence: confirmed identity joins and changed known states per domain. Risk: runtime arrays/FName/FString layouts differ from save serialization. Exit: displayed meanings have evidence and all fixtures remain byte-identical on no-op.

### Phase 3 — coherent existing-value edits (M)

Goal: practical editing without record construction/event skipping.

- [ ] Confirm nonzero gald, character identity, HP/SP and bought/consumed stack behavior on PS5 before promoting meanings.
- [ ] Expand existing ingredient/material/consumable quantity edits by category/effective cap; 99 needs validated active capacity. Decode CP current/max and level/flower/bonus dependencies before restore.
- [ ] Add selected-category refill and supported HP/CP restore with whole-batch validation and stable bindings available before lazy expansion.
- [ ] Keep XP/level/derived-stat presets unavailable until growth/dependencies are established; preserve unsupported values without normalizing.

Evidence: one-variable pairs and console load→effect→save→reload. Risk: context-dependent caps and valid bytes with inconsistent gameplay. Exit: grouped undo restores original plaintext/ciphertext, and later normal acquisition remains functional.

### Phase 4 — builds: equipment, accessories, artes, skills (L)

Goal: valid character builds with reversible operations.

- [ ] First swap only existing owned compatible weapon/armor/accessory instances; validate unique references, ranks, abilities and character restrictions.
- [ ] Decode title unlock, purchased skill, arte usage/unlock/proficiency and panel bonuses separately; show prerequisites before purchase/unlock controls.
- [ ] Add presets limited to available verified records with costs/dependencies, atomic application and one grouped undo.
- [ ] Only then fill a proven empty common-item slot; establish acquisition/order/capacity. Defer accessory creation, deletion/compaction and resizing until allocation/inverse state is known.

Evidence: equip/unequip, duplicate accessory, enhancement/transfer and individual skill-purchase pairs. Risk: identity namespaces and event bookkeeping. Exit: undo across view rebuilds preserves identity; crafting/acquisition/learning still work after reload.

### Phase 5 — completion, quests, maps and NG+ (L)

Goal: trustworthy completion assistance with isolated optional writes.

- [ ] Decode owl discovery/report/reward, fish catches/expert rewards and recipe knowledge/cooked records; never substitute aggregate play-record counters.
- [ ] Join quest objectives/stages/rewards/scenario prerequisites; initially provide read-only filters and missing-content guidance.
- [ ] Show artifact ownership/NG+ effects, treating score separately and DLC availability separately from gameplay toggles.
- [ ] Add maps only after asset/coordinate joins; every unlock/reward action needs complete dependency evidence and a separate console experiment.

Evidence: collection/report/reward pairs, quest stages and NG+ observations. Risk: cross-section progression flags can strand events. Exit: counts match game menus and supported writes preserve the next normal event/reward.

### Phase 6 — version coverage and qualification (L)

Goal: broaden support only after the base workflow is proven.

- [ ] Add measured region/build/base/DLC schema profiles; retain strict unknown-version rejection with explanatory messages.
- [ ] Study preview and SystemSave independently; use existing multi-file outputs only for proven dependencies and preserve conflict checks.
- [ ] Evaluate upstream fixtures as external oracles subject to rights; obtain owned expansion PS5 saves instead of assuming the documented 593,080-byte DLC layout transfers.
- [ ] During implementation, run plugin conformance, meaningful crypto/corruption/dependency/preservation/export/UI checks, then human qualification and per-feature support documentation. Only a human promotes Stable.

Evidence: late-game/NG+/DLC/region fixtures and console outcomes. Risk: content changes layouts and limits. Cross-platform conversion is a separate future scope, not required for these PS5 improvements.

## Requirements

| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
| --- | --- | --- | --- | --- | --- |
| TOA-R1 | Samples | Same PPSA02745 slot/build before/after one gald change, Apple Gel buy/use, HP loss/heal and SP gain/spend; observed menu values and unchanged control re-save. | P1/P3; all current gald values are zero. | Yes: verified scalar expansion | Current Inferred fields only. |
| TOA-R2 | Data | Versioned save-ID↔database-ID/UniqueId/label catalogue with localized names, categories, descriptions/icons, cap rules, equip restrictions and per-field provenance/licence. | P1–P4 named inventory and valid gear choices. | Yes: comprehensive semantics | Existing 13-name CSV and numeric fallback. |
| TOA-R3 | Human | PS5 tester to restore spare slots, load, observe effect, play normally, save/reload; record build/edit/expected/actual and later acquisition/progression. | P3–P6; reopen is not gameplay validation. | Yes: compatibility / Stable | Keep Experimental and uncertainty visible. |
| TOA-R4 | Core | Offline reference-pack import/cache separate from saves: bounded JSON/CSV/images, manifest/build/language/hash checks, rights display and local asset serving. | P2–P5 private game-derived data; importer currently handles saves. | Yes: user-supplied pack route | Embed approved small factual resources; text-only views. |
| TOA-R5 | UI | Searchable category/character-filtered item picker with optional icon/description, owned-instance identity, cap/availability reasons and unknown fallback. | P2/P4; enum currently uses native select. | Yes: rich picker UX | Filtered EnumSpec plus separate details/tables. |
| TOA-R6 | SDK | Read-only reference-pack access at document open without plugin I/O, including version/language/assets; preserve old parameterless plugin/Open contract. | P2–P5; Open(ISaveFile) has no reference context. | Yes with R4 route | Embedded resources; never place reference dumps in save outputs. |
| TOA-R7 | SDK | Optional reference/enum metadata for asset/category/availability reasons, plus Core indexing of referenced assets. | P4; EnumOption has only value/label/description. | No: enhancement | Existing enums/descriptions and standalone ImageNode. |
| TOA-R8 | UI | Render supported image/action/detail table cells, visible confidence/warnings, accessible row labels, category filters and compatible virtualization sizing. | P2/P4; current table suppresses non-field/text cells. | Yes for rich grid only | Ordinary text tables and standalone sections/assets/actions. |
| TOA-R9 | Tooling | Section-relative decrypt/diff aligning schema IDs/logical records; detect identical twins, use control re-saves and track evidence/dependencies. | P1 onward; physical blocks shuffle and cipher changes propagate. | Yes: reliable mapping | Inspected scratch decoder and manual comparisons. |
| TOA-R10 | Data | Growth/XP curves, base/current/effective stat rules, CP dependencies, skill costs/refunds/prerequisites and artifact/DLC cap rules. | P3/P4 coherent presets; static min/max are insufficient evidence. | Yes: dependent writes | Read-only XP/base values; independent proven edits only. |
| TOA-R11 | Tooling | Independently authored synthetic/rights-cleared edge/dependency/undo fixtures; sample tests skip if absent; retain independent cipher/no-op vectors. | P1–P6; no private saves/dumps committed. | Yes: safe implementation | Existing conformance/three-slot corpus. |
| TOA-R12 | Samples | Mid/late-game known party; single equip/unequip per gear type, two same-type accessory instances, rank/ability transfer and empty/full pool boundaries. | P2/P4 instance identity, equipment references and capacity. | Yes: equipment/accessories | Read-only IDs; separate common stacks from work data. |
| TOA-R13 | Samples | One level-up/herb, CP heal/rest/flower, arte use/proficiency, title unlock/skill purchase/panel bonus; normal vs valid 99-stack capacity state. | P3/P4 derived state/limits/unlock distinctions. | Yes: character/CP/skill writes | Source-labelled candidates/raw views. |
| TOA-R14 | Samples | Separate owl discovery/report/reward, recipe learned/cooked, fish caught/expert reward, quest stages/reward, camp/map unlock and NG+ artifact/carry-over saves. | P2/P5 actual state vs counters. | Yes: progress coverage | Linked guides/raw sections, no completion mutation. |
| TOA-R15 | Samples | Labelled late base/NG+/Beyond the Dawn/other-region-build PS5 saves with matching system siblings and new no-op/edit evidence per schema. | P6 strict size/schema currently excludes variants. | Yes per new variant | Advertise base PPSA02745 only. |
| TOA-R16 | Human | Select distribution route/permissions: factual mappings, attributed CC BY-SA pack, permitted tables or local owned dumps; decide compatible GPL use before code reuse. | P1 onward; several sources GPL or unlicensed. | Yes for affected redistribution | Independent implementation/observations and source links. |
| TOA-R17 | Data | Actor/quest/recipe/fish/artifact label→save identity→localized location joins; base/DLC coverage; separate map coordinates/layers/icon rights. | P2/P5 guide order is not identity. | Yes for named progress; maps later | Numeric unknowns and external links. |
| TOA-R18 | SDK | Optional generic map/marker and skill-dependency graph nodes with stable linked IDs and text fallbacks. | P4/P5; ViewNode has neither. | No: enhancement | Lazy tables/sections, approved static ImageNode. |
| TOA-R19 | SDK | Reversible structural transactions or documented fixed-slot scalar model, including inverse records and stable IDs across rebuilds. | P4 allocation/removal; current actions have scalar Changes or Undoable=false. | Yes for true structural undo | Existing fixed slots only; defer compaction/allocation. |
| TOA-R20 | Core | R19 undo/redo and refreshed field/action/asset indices across structure changes; reject stale/reused IDs/conflicting sibling writes. | P4/P6; current history replays ValueChanges. | Conditional: structural actions | Bind stable fixed slots/scalar inverse values; keep unsafe removals unavailable. |
| TOA-R21 | UI | Computed semantic action/preset preview: before/after, costs/caps, skips/dependencies, warnings and grouped undo. | P3/P4; static Confirm has no computed preview flow. | Complex presets only | Two-step plugin plan/apply with bounded text/table summary. |
| TOA-R22 | Human | Prioritize preview freshness/expansion/progress writes; observe recruitment and internal Grade/Score behavior. | P1/P5/P6 scope and meaning. | No for early phases | Preserve preview/system state; keep candidates Unknown. |

Framework/UI work belongs to its owner. Plugin implementation stays inside `plugins/TalesOfArise/` and records requests in its README; this research authorizes no framework edits. Reuse existing atomic batches, multi-file output, standalone assets, lazy/searchable tables and export/backup handling.

## Acceptance criteria

- [ ] Respect plugin boundaries and small composed C# files; document schema/title/build support, revisions/licences/confidence and SDK requests. No plugin-local repo tooling or private saves/dumps committed.
- [ ] Exact no-op bytes on three primary slots and three identical twins; report three snapshots honestly. Require independent no-op/crypto evidence for every additional schema.
- [ ] Edits repair current integrity/encryption, reparse and preserve unrelated plaintext, IDs/flags/padding and outer metadata unless separately proven writable.
- [ ] No Verified meaning based only on PC runtime offsets, matching zeros, guide order, nonzero-byte counts or aggregate records.
- [ ] Names use demonstrated identity namespaces/provenance; unknown IDs remain visible and unsupported edits unavailable.
- [ ] Reference packs declare version/language/rights and reject mismatches/bad joins; no plugin I/O and no unapproved copyrighted text/image bundle.
- [ ] Quantity edits obey category/effective caps, testing 0/1/cap/cap+1 and zero-quantity acquisition semantics; preserve above-policy originals without silent normalization.
- [ ] HP/CP/XP/level/skill operations update all proven dependencies atomically or remain unavailable; test publisher limit facts on the supported PS5 schema.
- [ ] Equipment uses compatible owned identities; cover duplicate accessories, rank/skill edge cases, full pools and later crafting/acquisition after reload.
- [ ] Bulk actions exclude unsupported/key/story rows, preview changes/skips and undo as one operation, even before lazy pages are opened.
- [ ] Structural writes wait for allocation/acquisition/inverse-state evidence; test undo/redo across rebuilds and stale-ID rejection.
- [ ] Distinguish owl found/report/reward, quest objective/reward, recipe knowledge/cooked and fish notes/expert state. Count-only edits never claim full completion.
- [ ] Keep bounded/searchable views and existing open-time checks/256-word raw pages; rich controls have keyboard/accessibility and text fallback verification.
- [ ] During implementation, conformance and meaningful crypto/corruption/range/dependency/preservation/undo/export/UI checks pass; absent private samples skip. This research is not a fresh .NET test run.
- [ ] Export exercises changed-directory scope, originally identical backup mirroring and proven sibling dependencies; opaque SystemSave remains untouched.
- [ ] Record PS5 load→effect→normal acquisition/progression→save→reload per enabled feature family; only a human promotes Stable.

## Open questions & risks

- Are six level-5 sections recruited characters or preinitialized future members? Nonzero level alone is not an availability test; inspect PartyOrder/scenario state.
- Which stat fields are bonuses, growth inputs, effective totals or caches? The prefix offset relationship does not justify an effective-stat editor.
- Where are CP current/max and level/flower/bonus dependencies? Increasing a CP-related key-item quantity is not a supported CP operation.
- What are inventory `0x41`, obtained-once/new/order flags and instance-pool rules? Existing stack edits can affect later acquisition; reproduce the PS4 failure scenarios independently.
- Why do owl section bytes vary with a zero candidate counter? Padding/transformed IDs/cache state are possible, unproven explanations. Collection/report/reward and alternate spawns need mappings.
- Does internal GradeShop contain unused compatibility state, score or artifact switches? No player-facing grade balance/offset is established.
- Does stale load-screen preview merely confuse users or affect loading? No console result yet demonstrates another integrity dependency.
- Updates/regions/localization/DLC/Switch 2 can change IDs, layouts and caps. Converter fixtures and engine similarity remain leads, not support claims.
- Public availability does not license copying. Keep code licences, factual mappings, adapted wiki prose and copyrighted media separate in the manifest and owner decisions.
- Cached code blobs were checked against pinned upstream sources; some live pages/APIs were inaccessible. No complete icon dump/API route or absence of alternative editors was proven. No downloaded implementation was executed or private save uploaded, and no repository files were modified.
