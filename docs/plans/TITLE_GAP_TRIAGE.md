# Remaining title work: issue #81 triage

Research snapshot: **2026-10-30**, code/main `0126f8f41c678c792f6206c11c4a5bb2bf9f0925`.
This disposes of the 17-title table in [#81 section B](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/81).
The per-title plans are historical; current code and tests take precedence over their unchecked proposals.
Each row has exactly one disposition: **READY** (bounded implementation with existing evidence),
**NEEDS-CAPTURE** (specified observations/joins), **NEEDS-INTEGRITY** (specified unresolved envelope or
integrity), or **OUT** (no justified implementation investment). These are gaps, not promises of writers.

All capture rows feed [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
For each event, export the complete same-slot before/after set and matching backups, plus a separate
load/save/reload **no-action control**. Record title, build, region, DLC/mode, slot, action and visible
before/after values; archive order and backup twins do not prove a transition. For acceptance, first
restore an unchanged control, then load the edited spare slot, observe/use the change, re-save, reload
and re-export (B2). Supply build/locale-matched reference provenance and applicable rights/owner-decision
basis alongside any requested data (B3); a menu capture alone does not license reference media.
See the [owner checklist](../OWNER_CHECKLIST.md), [capture protocol](../SAMPLES.md) and
[licence register](../LICENCE_EXCLUSIONS.md). Existing warned writes remain enabled; retain
Experimental/Inferred/Unknown. Only the owner promotes Stable.

This pass read production code, committed tests, READMEs and the linked plans; it did not execute
plugin suites or renew their historical sample/console claims. No private samples were opened or copied.
READY decisions below use code and authored-fixture infrastructure already committed. Future sample
checks must use `SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`;
never commit samples or extracted values. The two READY tasks are independent; Gearbox has higher user value.

## AstroPlayroom

[README](../../plugins/AstroPlayroom/README.md) · [plan](AstroPlayroom.md) ·
[field whitelist](../../plugins/AstroPlayroom/SaveToolkit.Plugins.AstroPlayroom/AstroFields.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Special bots and newer structural grammar | NEEDS-CAPTURE | Same-profile pre/post update, then separate event-eligibility and each bot discovery/rescue pair; include added-artifact acquisitions and both games around an actual Playroom→Astro Bot transfer. Current fields whitelist existing primitives; inserting community `Armadillo` keys is unsupported. Establish counts, sibling ordering and dependent fields before any structural writer. |
| Physical collectible/world joins and progression dependencies | NEEDS-CAPTURE | One named artifact, prop prize, stage clear and boss clear separately; one puzzle pickup on each ambiguous 5/3-prefix stage boundary listed in the README. Record physical location, museum/piece counts, cinematic/notification/reward effects and all changed flags. Candidate aliases/world presets already ship; they do not prove physical membership. |
| Coin limits and edited-allocation acceptance | NEEDS-CAPTURE | Separate known coin gain and one gatcha spend, then cap/clamp and spare-slot edit trials. Return primary/backup through B2, including suffix preservation. Reparse checks establish storage preservation, not game acceptance; no located checksum/signature is established. |

## AtomicHeart

[README](../../plugins/AtomicHeart/README.md) · [plan](AtomicHeart.md) ·
[existing-slot model](../../plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart/InventoryState.cs) ·
[stored-frame writer](../../plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart/SlotDataWriter.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Inner integrity | NEEDS-INTEGRITY | Outer file SHA-1 is implemented. Decoded SlotData native/unrecognized payloads have no established inner integrity field, coverage or duplicate-state repair rule. Need an independently established check/coverage model or native acceptance evidence that narrows this uncertainty; outer SHA-1 and self-reparse do not resolve it. |
| Original codec agreement and stored-output acceptance | NEEDS-CAPTURE | Return an independent reviewed decode comparison for the original Kraken frames; separately B2-test one small existing Amount edit, recording file growth, slot capacity, inventory use and travel. Current stored-block decoding/oracle checks do not establish the PS5 loader's acceptance. |
| Equipment, upgrades, skills and blueprints | NEEDS-CAPTURE | Separate buy/refund one named skill, equip one weapon, upgrade one module and learn one blueprint. Record costs, AP/resources, slot/instance and menu effects; supply matching native skill/module IDs, prerequisites and build/locale data. Current skill collections do not identify these states; existing Amount/asset edits are not coupled loadout transactions. |
| Structural inventory | NEEDS-CAPTURE | Move one identified stack backpack→NORA and back separately; acquire into an empty cell, split/drop/use the last item separately. Capture ItemId, cell/order, Amount, all affected containers and equipment references. Current slot identities and sentinel recognition do not establish allocation/removal grammar. |
| Story, Chirpers and Testing Ground rewards | NEEDS-CAPTURE | One Chirper pickup, objective advance, Testing Ground completion and reward claim per pair, with named location/reward and later re-save. Establish event/ownership/reward joins; the saved-level index and reference graphs already ship without completion authority. |
| Native/proprietary codec bridge proposed in the old plan | OUT | The managed Kraken route already exists; adding runtime native/process/service I/O has no demonstrated value for this supported subset and would not solve the evidence gaps above. |

## BaldursGate3

[README](../../plugins/BaldursGate3/README.md) · [plan](BaldursGate3.md) ·
[NewAge inventory references](../../plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3/LsmfInventory.cs) ·
[document/write boundaries](../../plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3/BaldursGate3Document.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Authoritative character, gold, base-stat and spell identities | NEEDS-CAPTURE | Separate visible-owner gold gain/spend, one base-score/respec change versus gear boost, damage/heal/temp-HP, learned spell and prepared spell changes. Include multiplayer/companion owner context and matching template/Stats IDs. Software party/storage joins already exist; cache plausibility and SaveInfo metadata do not establish gameplay authority. |
| Structural inventory/equipment | NEEDS-CAPTURE | One settled stack split/merge, acquisition/removal, cross-container move and equip/unequip per pair, including same-template duplicates. Trace entity generation/allocator/relocation, container keys, contributions and all equipment references. Existing positive-quantity edits and two-key swaps do not implement these transactions. |
| Story, approval and dependency transactions | NEEDS-CAPTURE | One named quest objective/branch, approval-changing dialogue, camp trigger and reward claim separately, returning Globals/NewAge/Osiris together. Need exact database tuple/entity/quest identities and reward/reversal dependencies; the Story view only inspects markers. |
| NewAge opaque header/integrity authority | NEEDS-CAPTURE | Compare same-slot controls and isolated gameplay events retaining the complete NewAge header, especially its unexplained u64 at +8. Establish its role before calling it a checksum/key/signature. LSPK v18 digest repair already exists; there is no demonstrated additional algorithm to implement. |

## Cyberpunk2077

[README](../../plugins/Cyberpunk2077/README.md) · [plan](Cyberpunk2077.md) ·
[full equipment identity joins](../../plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077/EquipmentData.cs) ·
[progression writer](../../plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077/ProgressionEditor.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Equipment, cyberware, attachments and structural items | NEEDS-CAPTURE | Separate owned-instance equip/unequip, implant install/remove, attachment swap, upgrade and stack split/drop/acquisition. Include duplicate base items; record seed/counter/flags, slot, capacity/cost, modifiers and effects. Full owner-1 identity readers already ship; base TweakDBID alone cannot authorize replacement or creation. |
| Perk/respec and coherent level-up transactions | NEEDS-CAPTURE | Buy/refund one named post-2.0 perk, cross a tier/capacity prerequisite, allocate one Relic node and gain exactly one level in separate pairs. Record spent/unspent balances, thresholds, rewards, ability/effect packages and StatsSystem mirrors. Existing raw progression/point setters and attribute mirrors do not model these consequences. |
| World, garage, fast-travel and story transactions | NEEDS-CAPTURE | Separate vehicle acquisition/summon, travel-point discovery, named quest objective/completion/reward and reversal where naturally possible. Capture facts, journal, mappins and persistency together. Absent arrays/facts are distinct from stored zero; current read-only unions and raw fact setters do not establish unlock/repair recipes. |
| Gameplay/slot-metadata/profile ownership | NEEDS-CAPTURE | Export sav.dat, slot metadata and user.gls around one level change and one profile/global change separately; test which member the native game regenerates. Prove an exact cross-file invariant before coordinated writes. No known CSAV container checksum is missing; separate user.gls integrity/role remains unestablished. |

## EldenRing

[README](../../plugins/EldenRing/README.md) · [plan](EldenRing.md) ·
[equipment readers](../../plugins/EldenRing/SaveToolkit.Plugins.EldenRing/EquipmentReader.cs) ·
[exact capture matrix](../../plugins/EldenRing/Evidence/OWNER_CAPTURES.md)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Attributes, respec and derived vitals | NEEDS-CAPTURE | Separate one Vigor/Mind/Endurance gain, HP-affecting equip/unequip, temporary buff and real respec; record starting class, all attributes, both level mirrors, current/effective/base HP/FP/stamina and load. Include Larval Tear acquisition/consumption and respec availability. Matching PARAM row IDs do not validate payload formulas or class minima. |
| Equipment, upgrades, Ashes and allocation | NEEDS-CAPTURE | Owned-weapon equip/unequip, active-hand selector, quick/pouch/spell/talisman-slot change, normal/somber upgrade and affinity/Ash change separately. Add same-base distinct instances, new allocation/last-item removal, storage in/out and highest-upgrade disposal. Establish every live/menu mirror, handle/free-list, count and matchmaking-history dependency; reader agreement is insufficient. |
| Tracked-value/internal integrity | NEEDS-INTEGRITY | The PS wrapper has no PC per-slot MD5, but the disputed internal tracked-value/hash block has no located PS5 coverage/inputs/recomputation or loader proof. Need build-matched validation of that block before new stat/equip writers; current preservation and preview do not resolve it. |
| World/boss/quest dependencies | NEEDS-CAPTURE | Separate named grace activation, boss defeat, reward claim, NPC stage/branch and world transformation; record event IDs and actual state before/after. Source candidate labels/maps already ship; do not infer quest edges or summoning-pool status from geographic agreement. |

## FinalFantasy16

[README](../../plugins/FinalFantasy16/README.md) · [plan](FinalFantasy16.md) ·
[dense allocator reader](../../plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16/FaithInventory.cs) ·
[ability reference view](../../plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16/FaithAbilityView.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Allocator/free-list and active-bank authority | NEEDS-CAPTURE | Buy the first new item, sell its final stack, reacquire, allocate a second distinct item and reuse a slot separately; include capacity boundary, both banks, equipment and shortcuts. Repeat active-bank observations for child/adult and stage replay. Code accepts substantiated dense keys only; free-list creation/deletion is unmodeled. |
| Saved-ability joins, AP costs and assignment | NEEDS-CAPTURE | Learn/upgrade/master/reset one named ability separately, recording AP and Eikon/slot; repeat named IDs already published in the README and one Eikon unlock independently. Return rank bytes/new/protect flags plus matching Nex definitions. Reference slugs and saved rank indices are distinct namespaces. |
| Equipment/growth/reward dependencies | NEEDS-CAPTURE | Separate each equipment-slot swap, pouch potency/capacity upgrade, XP-without-level, single level-up and Renown reward claim; record all banks, stats, AP, quest/reward flags and mode/DLC. Fixed-width setters and repaired Faith CRC already exist; they do not reconcile these systems. |
| Blanket storage-registration/helper migration | OUT | Existing primary bindings are independent of paging. No measured user or allocation benefit is established for replacing that implementation merely because an SDK helper exists. Reopen only with a concrete consumer need or measurement. |

## FinalFantasy7

[README](../../plugins/FinalFantasy7/README.md) · [plan](FinalFantasy7.md) ·
[limited owned-weapon setter](../../plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7/WeaponEquipment.cs) ·
[shipped preset adapter](../../plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7/FinalFantasy7PresetDocument.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Native sockets and loadout allocation | NEEDS-CAPTURE | Per Remake/INTERmission/Rebirth retail/demo, separately swap owned weapon/armor/accessory, move ordinary materia between sockets including a linked pair, and change a summon slot. Record owners, instance/detail IDs, slot capacity, proficiency and folio state; include duplicates and swap-back. Existing Rebirth weapon-reference edits preserve sockets; five semantic preset actions do not allocate native loadouts or add preset weapon assignment. |
| Upgrades, folios, level rewards and summon progression | NEEDS-CAPTURE | Separate weapon upgrade/proficiency unlock, folio purchase/refund, XP tier crossing and ordinary materia star crossing. Capture Rebirth summon-tier change with AP unchanged separately. Need exact per-build derived stats/abilities/cost/reward crosswalks; existing AP/star and level/XP pairs already have bounded writers. |
| Rewards, cards and structural inventory | NEEDS-CAPTURE | Separate acquisition of a new item/materia/weapon, last-copy removal, card pickup, booster opening, deck change, opponent win, world-intel completion and reward claim. Trace fixed-slot allocation, detail rows, counters, ownership/acquisition flags and reward actors. Existing counts/candidate-bit writes and relationship views are not complete transactions. |
| Internal RESD/related-record integrity | NEEDS-INTEGRITY | No internal checksum/signature algorithm or coverage is established for the edited gameplay records; rebuilding the outer container and reparse is not authentication. Need independent build-specific integrity/loader evidence, preserving MLSD/NVSD/common roles separately. Do not invent a checksum from unexplained bytes. |

## Gearbox

[README](../../plugins/Gearbox/README.md) · [plan](Gearbox.md) ·
[item planning](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox/Items/GearboxItemActions.cs) ·
[transfer planning](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox/Groups/GearboxTransferActions.cs) ·
[inventory identity lifecycle](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox/Items/GearboxInventory.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| GB-01: production action effects and reliable structural review (#81 A) | READY | Adopt `IActionEffectDocument`/`PlannedChange.Effect` for existing BL3/TTWL standalone/group import, duplicate/remove and deposit/withdraw/claim. Retain canonical parsed scalar/count/presence/slot metadata; replace fake import/absent structural field authority with ordered typed effects. Make cold/warm planning identity-pure; validate destination, ownership, moved-ID uniqueness and capacity before mutation. Preserve complete rollback/history/rebinding. Files and production-provider acceptance below; no new gameplay writer or BL4 support. |
| Legal parts, generated effects and class/skill builds | NEEDS-CAPTURE | Separate same-item regular-part/anointment/reroll changes and named passive allocation/respec, with exact card, class/tree/rank, costs, DLC/hotfix/build and native subobject IDs. Supply versioned balance→part pools and prerequisites with provenance; category/bit-width validity and calculator order do not prove legality or a saved skill crosswalk. |
| Account/profile precedence and Guardian/Myth dependencies | NEEDS-CAPTURE | Export matching profile plus every affected character before/after one Guardian token/perk, then after loading each character without other actions. For level-40 Myth capture one allocation per tree, rotation and capped-stat boundary separately, recording XP/rank/points. Existing groups intentionally expose mirrors without choosing authority. |
| Mission/collectible repair | NEEDS-CAPTURE | Named mission objective/branch, completion, reward claim and collectible pickup separately, retaining character/profile/backups together. Need exact mission/opaque-subobject IDs, reward-once and reversal dependencies; named reference/checklist views already ship. |
| BL4 file roles | NEEDS-CAPTURE | New character creation, no-action control, one known currency change and one legitimate pickup separately; export the entire same-build directory set, settings/profile and backups to identify which files change and their roles. Follow [BL4 checklist](../../plugins/Gearbox/BL4-feasibility.md); the current blob is not proven character gameplay. |
| BL4 PS5 inner envelope | NEEDS-INTEGRITY | PS5 codec/derivation, compression/framing, padding and any trailer coverage remain unestablished; PC Steam/Epic algorithms do not identify this envelope. Require lawful local codec evidence and independent no-op/validation across role-identified samples. Do not request secrets or try account-derived material in reports. |
| Platform conversion, premium entitlements and exact combat simulation | OUT | No supported dependency/entitlement model or justified bounded user workflow; these are historical non-goals, not work unblocked by action effects. |

GB-01 code check: neither production document declares the
[effect interface](../../src/SaveToolkit.Sdk/IActionEffectDocument.cs).
`GearboxMetadata.TryActionObservation` fabricates `import/serial`; import planning emits it with
`OldExists = false`. Transfer planning emits destination identities absent from parsed fields.
`GearboxInventory.EnsureCapacity` calls `EnsureIdentities`, which advances `_nextId`; transfer selection
also visits `Entries`. Existing group mementos provide the starting mechanism, not proof of pure planning.
[MetadataR6Data](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests/MetadataR6Data.cs),
[MetadataR7Data](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests/MetadataR7Data.cs) and their
authored fixture builders already support production documents; existing
[group-write tests](../../plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests/GroupWriteTests.cs)
also show the export/history paths. These tests were read, not rerun; synthetic renderer acceptance alone
does not satisfy this new production-provider task. Host/API/web acceptance changes require their owning scope.

GB-01 implementation files: the linked planners/inventory, `GearboxDocument.cs`, `GearboxMetadata.cs`,
`Groups/GearboxGroupDocument.cs` and `Groups/GearboxGroupIdentity.cs`; plugin regressions extend the
authored fixture helpers above. Add real Gearbox host-session/API cases beside
[Core action-effect tests](../../tests/SaveToolkit.Core.Tests/ActionEffectTests.cs) and production-provider
browser cases beside [the synthetic suite](../../web/e2e/action-effects.e2e.ts).
Require nonempty import/deposit/withdraw/claim fixtures, exact ordered effect/cardinality assertions,
absence/null and long serials, genuine role/path, preview/cancel/stale/rejection/rollback, one undo/redo
unit, every owned output once, complete-byte export/reopen and current-candidate bindings. Exercise
keyboard/focus and 375/768/1024/1440 widths; update the owning mandatory browser inventory if adding cases.

## GenericJson

[README](../../plugins/GenericJson/README.md) · [plan](GenericJson.md) ·
[array actions](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson/JsonArrayActions.cs) ·
[structural preflight](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson/JsonStructureActions.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| GJ-01: cross-container JSON move, syntax-only subset | READY | Add same-document **array→array** subtree move by live source/destination identity and insertion index. Reuse clone/preflight, mementos and token writer; preserve subtree IDs/lexemes/duplicates, reject self/descendant destinations and stale/type/bounds/size/depth/review failures atomically. Files: linked array/structure actions, `JsonTokenTree.cs`, `JsonTreeWriter.cs`, `JsonSaveDocument.cs` and focused `JsonSessionFixture` tests. Existing `move` only reorders one array; no cross-file/object-member move or title-safe inventory transaction. |
| Title definitions, absent-key templates and dependencies | NEEDS-CAPTURE | Cyberpunk: one enum/scalar/default reset and dynamic language/HDR choice per pair, including a second settings version, to prove value/index authority. It Takes Two: fresh profile, one minigame discovery/play and separate Cody/May score/win, chapter/checkpoint transition; record Continue/replay effects. Ridge Racer: one PSP wrapper filter/upscale/AA change per pair with wrapper build. Return exact raw-key/menu-label joins and required/optional defaults; current present-key presets already ship. |
| Reference catalogues and rights | NEEDS-CAPTURE | Alongside the above events supply exact build/locale label→raw-key maps, complete checkpoint/minigame aliases, pinned source/provenance and applicable reuse basis. Existing six-record Cyberpunk pack and original word splitting are implemented; other title IDs do not inherit their definitions or reference-media rights. |
| Companion ownership | NEEDS-CAPTURE | Capture all members around one suspected shared change; demonstrate a minimal cross-file invariant and whether a one-member edit is overwritten/rejected while a coordinated one persists. Profile/CloudSettings/LocalSettings are sections, and identical backups/co-location do not establish F7 ownership. |
| Full JSON Schema engine or blanket enum/helper rewrite | OUT | Current explicit local-schema subset and exact-null/union/search/breadcrumb/reset support already cover the delivered workbench. No concrete title requirement justifies a full standards engine or a migration for its own sake. |

GJ-01 code check: `JsonArrayActions.Mutate` removes/inserts within its single `array` argument;
no destination container exists. The [tree](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson/JsonTokenTree.cs)
already clones/reindexes live parent links without replacing stable node IDs, and the
[writer](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson/JsonTreeWriter.cs) serializes changed
memberships from retained token slices and validates the result. The
[document](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson/JsonSaveDocument.cs) already owns
structural rollback and snapshot restoration. Extend
[array-edit/history tests](../../plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests/JsonArrayEditingTests.cs);
existing same-array coverage is infrastructure, not evidence that cross-array moves already work.
Acceptance: pure preview, both parent reviews, destination trivia, overlay rematching, stale identities,
mixed scalar/structural undo/redo and exact original bytes after undo, with byte-preserving reopen.

## XorJson

[README](../../plugins/XorJson/README.md) · [plan](XorJson.md) ·
[transform/version routing](../../plugins/XorJson/SaveToolkit.Plugins.XorJson/JsonEncoding.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Gameplay identities, legal effects and console semantics | NEEDS-CAPTURE | Offline same-version pairs for currency gain/spend, XP/level, pickup, equip/unequip, rarity/effect/enchantment change, quest step/reward and station discovery separately. Include merchant versus bag/equipped copies, repeated types, reorder and nonempty effects; record seed, TypeTags, slots and exact visible behavior. Existing raw/scalar/structure and gameplay actions do not repair rewards/effects/loadout dependencies. |
| Transform/envelope and version evidence | NEEDS-CAPTURE | One master-volume change and no-action control returning original encoded global plus plain character files; repeat on another supported build/new hero. Supply a genuine PS5 XOR-1 file with a controlled setting change before extending that transform's title claims: current XOR-1 evidence is synthetic. No decoded checksum/signature is identified; investigate external integrity with unchanged-restore/B2 controls rather than inventing one. |
| Global/character authority | NEEDS-CAPTURE | Export both files before/after active-character switch and one suspected shared progression event separately. Prove slot membership and the exact fields/invariant that persist across native re-save; current F7 staging validates software membership, not native shared-state precedence. |
| Reference joins, limits and media rights | NEEDS-CAPTURE | Match captured PS5 item/skill/effect names and limits to exact TypeTags/catalog IDs on the same build, with pinned provenance and applicable reuse basis for any additional dataset. Existing attributed PC references are not proof of PS5 legal pools, caps or new asset rights. |

## GodOfWarRagnarok

[README](../../plugins/GodOfWarRagnarok/README.md) · [plan](GodOfWarRagnarok.md) ·
[finite integrity probes](../../plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok/RagnarokIntegrity.cs) ·
[field discovery](../../plugins/GodOfWarRagnarok/FIELD_DISCOVERY.md)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Save integrity | NEEDS-INTEGRITY | The gameplay body's authenticated/checksummed spans, embedded field location, seed/encoding and any keyed/external validation remain unknown. Recorded CRC/digest searches have no match; the EOF sentinel and field-name hashes are not body checksums. Need a demonstrated verification/repair rule plus unchanged/edited loader controls, not another unbounded guessed-hash sweep. |
| Gear/skill/XP grammar and ownership | NEEDS-CAPTURE | One named armor/attachment equip, upgrade, enchantment-slot unlock, skill purchase/refund and XP gain/spend per pair. Record character, instance/parent, definition, costs and stats; supply matching asset definitions. Definition-hash names/catalog levels are read-only evidence; instance index is not an upgrade word and runtime skill constants do not identify saved bits. |
| Story, flags and map grammar | NEEDS-CAPTURE | Separate named quest objective/branch, collectible pickup, reward claim and travel discovery with visible state and the full surrounding nested spans. Need length/type rules and exact flag/directory→entity joins; wallet/flag indexes and reference maps do not establish transactions or completion. |

## Insomniac

[README](../../plugins/Insomniac/README.md) · [plan](Insomniac.md) ·
[read-only inventory grammar](../../plugins/Insomniac/SaveToolkit.Plugins.Insomniac/InventoryReader.cs) ·
[capture checklist](../../plugins/Insomniac/OWNER-CAPTURES.md)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Inventory ownership, equipment and structural identity | NEEDS-CAPTURE | One weapon pickup, equip/unequip, ammo use/refill, armor pickup and cosmetic equip separately, identifying Ratchet/Rivet/shared ownership and repeated instances. Trace GUID/config/container references and list changes. Full-mask decoding/config-name joins already exist; they do not establish authoritative ownership or an allocator/cleanup writer. |
| Currency and upgrade joins | NEEDS-CAPTURE | One bolt gain/purchase and raritanium gain/named-node purchase separately, recording HUD balances, price, node/grid and both lifetime counters. Add one Omega upgrade in Challenge Mode and matching CurrencyItemConfig/WeaponGridConfig/WeaponModConfig instances with caps/costs/prerequisites. Career-minus-spent is a diagnostic, not the wallet. |
| Location-resolved collectibles | NEEDS-CAPTURE | One named gold bolt, spybot, Lorb, CraiggerBear and Zurpstone per location pair; include dimension/checkpoint/objective context and awarded effects. CurrentLevel is not a validated planet index; collection-bit counts do not locate individual pickups or establish map transforms. |
| Body integrity | NEEDS-INTEGRITY | No save-body/section integrity field or coverage/repair algorithm is established for DAT1/DDL/inventory. Name CRCs identify fields/assets only. Require native unchanged/isolated-edit controls or independently demonstrated body verification; do not reuse name hashes as checksums. |

## Pragmata

[README](../../plugins/Pragmata/README.md) · [plan](Pragmata.md) ·
[fixed-size document](../../plugins/Pragmata/SaveToolkit.Plugins.Pragmata/PragmataDocument.cs) ·
[DSSS writer](../../plugins/Pragmata/SaveToolkit.Plugins.Pragmata/Crypto/DsssContainer.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| RSA/key trailer authority | NEEDS-INTEGRITY | The preserved 128-byte trailer's verification/generation and authenticated coverage are unimplemented; RSA enforcement by PS5 is unproven. Mandarin key/IV consistency, per-block CityHash64 and outer Murmur3 are already checked/repaired and do not establish trailer validity. Need lawful verification/loader evidence; no secret material in reports. |
| Structural resize | NEEDS-CAPTURE | Acquire a first new inventory entry, remove its last copy, reorder/equip and cross a variable-length structure boundary in separate same-slot pairs. Establish count/offset/reference changes, block-capacity/key-metadata regeneration and trailer changes from original files. Current `DsssContainer.Rebuild` rejects changed plaintext length; there is no structural serializer to enable. Trailer integrity is a separate row above. |
| Named items/loadouts/progression | NEEDS-CAPTURE | One named item gain/spend, weapon/skill equip, perk unlock, objective transition and checkpoint event per pair, with exact UI name, raw ID and dependent fields. Existing quantity/clock/primitive writes and references already ship; identifier hash matches do not establish item meanings, caps or unlock recipes. |

## SuckerPunch

[README](../../plugins/SuckerPunch/README.md) · [plan](SuckerPunch.md) ·
[Yōtei planning](../../plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch/YoteiPlanning.cs) ·
[manual caption ownership](../../plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch/SuckerPunchCaption.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Yōtei checksum tail and restore-imported action | NEEDS-INTEGRITY | All supplied tails are zero, so declared-end and physical-EOF additive coverage agree. Need an independently verified game implementation or accepted naturally nonzero-tail capture/control distinguishing endpoints and any additional dependency, as specified in [Yōtei capture 7](../../plugins/SuckerPunch/YOTEI_FORMAT.md#exact-owner-captures-for-31). Production candidate reparse and synthetic tail mutations are not independent proof. Restore-imported remains deferred; existing warned quantities/target action stay as implemented. |
| Yōtei gameplay authority | NEEDS-CAPTURE | Separate one resource/ammo gain/use, equip/upgrade, technique purchase, timed no-action interval, difficulty change and quest/collectible/reward event. Record UI values and every changed registry/primary/secondary field. Constant gear-name registry entries do not establish ownership; timer, health/spirit, difficulty and progression remain unjoined. |
| Tsushima gear, techniques and world dependencies | NEEDS-CAPTURE | Separate owned gear/charm equip, upgrade, technique purchase/refund, rank increase, collectible pickup and quest/reward event, recording slots/costs/flags and reverse changes where possible. Existing resource/ammo/timer writes and relationship/reference views do not establish these coupled writers; v47 timer location also needs a timed control. |
| Automatic timer/difficulty→SUBTITLE synchronization | NEEDS-CAPTURE | Export exact gameplay file plus that directory's param.sfo before/after a timed no-action save and one difficulty change separately, across relevant slot roles/builds/locales. Establish producer/authority, time rounding, localized text format and native regeneration on re-save. Manual UTF-8 caption capacity/ownership/history tests already ship and do not prove automatic gameplay synchronization. |

## TalesOfArise

[README](../../plugins/TalesOfArise/README.md) · [plan](TalesOfArise.md) ·
[existing set/restore actions](../../plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/AriseInventoryActions.cs) ·
[exact owner checklist](../../plugins/TalesOfArise/OWNER_CHECKLIST.md)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Equipment/accessory identity and allocation | NEEDS-CAPTURE | One owned weapon/armor equip/unequip per character; two same-type accessories with different rank/abilities, then one enhance/ability transfer separately. Include empty/full pools, first acquisition, zero/reacquisition and capacity-artifact states. Need saved instance/slot/source-consumption joins, allocator/flags and costs, not catalogue order. |
| CP and character growth | NEEDS-CAPTURE | Separate CP healing use, rest, Orange Gel use and Astral Flower acquisition, with current/max CP and active bonuses; separate one level gain, herb use, recruitment and party-order change. Record XP and base/effective stats. Existing inferred CP pair is read-only; six preinitialized level blocks do not prove recruited characters or growth transactions. |
| Artes, skills, progression and rewards | NEEDS-CAPTURE | One arte use/proficiency threshold, title unlock, paid node and final-panel node separately, recording SP/cost/bonus. For world state separate owl find/report/reward, quest objective/completion/claim, recipe learn/first cook, new fish/reward and artifact acquire/toggle/NG+ carry-over. Need exact saved-ID and related-state joins before transactions. |
| Existing write/preview acceptance and variants | NEEDS-CAPTURE | B2-test existing quantity set **and restore-imported** workflows plus each scalar family; include later acquisition after zero edits and header/load-screen preview behavior. Ciphertext SHA-1/encryption self-checks already work. Supply independent mid/late/NG+/DLC/build pairs before extending schema coverage; unknown outer preview authority is not a known missing checksum. |
| Invented Grade currency | OUT | No player-facing saved Grade balance is established; internal GradeShop naming supplies neither a field nor a useful bounded writer. Investigate artifact ownership/effects through the explicit progression captures instead. |

## UnrealGvas

[README](../../plugins/UnrealGvas/README.md) · [plan](UnrealGvas.md) ·
[envelope reader](../../plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/Gvas/GvasEnvelope.cs) ·
[limited structural editor](../../plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/Gvas/GvasStructuralEditor.cs) ·
[Hogwarts evidence](../../plugins/UnrealGvas/HOGWARTS.md)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Jedi/Sackboy integrity | NEEDS-INTEGRITY | Resolve Jedi `Crc32` actor/container coverage, serialization and seed, and Sackboy General `Checksum` separately. Existing negative whole-file/zeroed-field vectors and scalar reparse do not identify either algorithm or rule out per-actor checks. No standard GVAS checksum can substitute. |
| Wrapped-title rewriting | NEEDS-CAPTURE | Real wrapped PS5 same-build no-action and one scalar-setting pair, retaining complete RFC1950/Astroneer envelope, declared sizes and trailer. Establish game-specific zlib window/framing/extension requirements and B2 rewritten acceptance; current wrapper fixtures are synthetic/read-only. Unknown outer Oodle envelopes need role/signature/decode evidence before being claimed. |
| Hogwarts row/image authority | NEEDS-CAPTURE | Export RawDatabaseImage and RawExclusiveImage together around one talent buy/refund, stack pickup/use, equipment swap and Field Guide/quest reward separately. Map exact table/column/key to the UI and determine which image owns, mirrors or regenerates it. Storage allowlists/catalog joins and read-only talent summaries already exist; updating both images blindly is unjustified. |
| Hogwarts stored-frame/slot acceptance | NEEDS-CAPTURE | First B2-test a semantic-no-change stored-output candidate on a spare slot, then one small allowlisted cell edit. Record file growth, slot budget, load/use/save/reload and re-export both databases with unrelated-row comparison. Managed decode, SQLite integrity checks and the editor's size budget do not establish native acceptance. |
| Structural/game-specific identities and opaque layouts | NEEDS-CAPTURE | Separate actual item creation/removal/reorder, native map/set change, complex text change and actor-spawn/despawn, with build-matched defaults and persistent keys. Current primitive remove-last and ordinal scalar paths do not supply semantic identity/cleanup for these operations. |
| Blanket SDK-31 workspace/collection/catalog migration | OUT | Require measured allocation or concrete behavior benefit first. Current codec/database paths and eligible canonical relationship views are implemented; availability of shared APIs alone is not a remaining title feature. |

## Witcher3

[README](../../plugins/Witcher3/README.md) · [plan](Witcher3.md) ·
[native inventory reader](../../plugins/Witcher3/SaveToolkit.Plugins.Witcher3/InventoryReader.cs) ·
[existing rank writer](../../plugins/Witcher3/SaveToolkit.Plugins.Witcher3/SkillRanks.cs)

| Remaining item | Status | Bounded task or exact missing evidence |
| --- | --- | --- |
| Structural identities, equipment and stash/Roach ownership | NEEDS-CAPTURE | Separate new-item pickup, split/merge, last-copy removal, equipment swap and player↔stash move, plus a distinct Roach-equipment change. Include same-name different-attribute instances; trace next-ID/unique-ID allocation, tags, slots and all component references. Existing quantity/durability/refill/repair writers do not allocate or clean up instances. |
| Respec, mutations and level-up transactions | NEEDS-CAPTURE | Separate paid skill rank, socket/unsocket, mutagen/mutation unlock, real respec and one level gain, recording free/used points, prerequisite/slot/effect/stat/reward state. Existing non-core rank setters and raw level/XP fields do not execute the game's related-system logic. |
| Gwent, recipes and story/world dependencies | NEEDS-CAPTURE | Separate card acquisition, deck edit, card-related quest reward, recipe learning, quest objective/branch/completion and reward claim. Capture collection/deck/facts/journal/thread/entity state together; include a timed no-action control for nonzero fact words. Individual/batched Gwent count setters already ship; neither counts nor parsed fact records establish reward-safe transactions. |
| Additional native schemas and companion roles | NEEDS-CAPTURE | Build-labelled late-game/NG+/DLC/Ciri samples plus no-action and one matching event per schema, preserving .req and all components. Need alternate component prefixes, money widths, saved-handle/array layout and any proven cross-file invariant. No known SNFH checksum is missing; opaque `magic_number` data and .req content lists must not become invented integrity/ownership rules. |
