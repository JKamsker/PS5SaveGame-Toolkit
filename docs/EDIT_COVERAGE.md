# Edit coverage gap analysis

Tracking: [#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40).
Reviewed **2026-10-29**, against local code snapshot
`0ae7fbed5aec8c6c24e9f5aed66f6eb7a3909ad1`. This is a report, not a change
to plugin permissions, confidence, maturity or supported versions.

## Scope and method

The baseline is [AGENTS.md](../AGENTS.md), the [plugin hard rules](PLUGIN_GUIDE.md#hard-rules),
[supported titles and write limits](USER_GUIDE.md#supported-titles-and-write-limits),
[SDK request triage](SDK_REQUESTS.md), and all 17 plugin READMEs linked below,
especially their enabled-write, evidence, integrity, remaining-work and source sections.
Private saves were not opened for this report. Byte claims below are **reported plugin
evidence**, not independently repeated measurements or new game validation.

Web searches covered each title family and its existing editors/guides. “Commonly
wanted” means recurring editor features or progression/collection goals documented
by their authors; it is a qualitative demand proxy, **not a survey or measured request
frequency**. Where no established title-specific editor was found, guides establish
the player goal only. PC tools, older-console tools and runtime tables do not prove
PS5 save offsets, legal limits or compatible writes. Proposed presets are our inference
from those goals, not claims that another editor implements the exact preset.

Every external reference used here has a URL and access date in [Sources](#sources).
Downloaded/community code is untrusted: research was text-only; no downloaded tool,
script, cheat table or executable was run. No save data, external code or assets are
added by this report. This is save-field coverage analysis, not a security review.

### Reading the tables

| Code | Meaning |
| --- | --- |
| **E** | **enabled** now, subject to the linked README's schema/row/range gates; not a proposed addition. |
| **R** | **read-only, with evidence**: a field or bounded candidate representation is located, but the requested writer is absent. Candidate-only rows explicitly identify the missing semantic join. |
| **N** | **not located**: no authoritative writable target for the requested meaning/operation is established. This does not mean its name or related bytes never occur. |
| **V / I / U** | Verified / Inferred / Unknown confidence. Storage identity and gameplay meaning are distinguished. These are not console acceptance ratings. |

Each demand row is counted once, even when it groups closely related edits. E rows
can have narrower coverage than their everyday names suggest; the exclusions get
separate R/N rows. Counts are **demand groups in this report**, not fields, individual
games, unsupported versions or an estimate of all possible edits. A raw U setter
does not count as a named gameplay feature. R candidate storage is not a promise that
the intended gameplay state has been identified.

For gap ratings, **value** H/M/L estimates player benefit; **effort** S/M/L means a
focused setter/action, a multi-component writer, or substantial format/dependency
research. **Risk** L/M/H is incremental implementation/data-consistency risk relative
to today's warned writer, not probability of console acceptance. “Research” means
it cannot yet be promised as an implementation task. Ratings are judgment calls.

Evidence codes used in every gap row:

- **B**: additional build/role-matched sample bytes, including positive/empty/sentinel cases.
- **C**: controlled owner before/after captures under [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31):
  one event, visible old/new values, exact build/region/DLC/slot, and a no-action control.
- **T**: console validation on a spare slot: load, check effect and unrelated state,
  use/spend/progress, native save, reload, re-export and diff. Every E row and every
  proposed addition still needs T; it is not silently waived when a gap row abbreviates it.

Existing warned writes do not wait for C/T. New semantic/structural writers may need
B/C to become implementable; warnings cannot replace missing ownership, allocation,
mirror, reward or dependency handling. No legal maximum is inferred from storage width.

## Per-plugin inventory

The summary is mechanically reconciled to the demand tables below. “Gaps” = R + N.
The separately ranked workflow additions do not inflate these field-demand counts.

| Plugin | E | R | N | Gaps |
| --- | ---: | ---: | ---: | ---: |
| AstroPlayroom | 3 | 0 | 2 | 2 |
| AtomicHeart | 2 | 2 | 3 | 5 |
| BaldursGate3 | 3 | 3 | 4 | 7 |
| Cyberpunk2077 | 5 | 3 | 1 | 4 |
| EldenRing | 2 | 4 | 1 | 5 |
| FinalFantasy16 | 4 | 0 | 4 | 4 |
| FinalFantasy7 | 4 | 4 | 1 | 5 |
| Gearbox | 5 | 2 | 1 | 3 |
| GenericJson | 6 | 0 | 3 | 3 |
| GodOfWarRagnarok | 2 | 1 | 4 | 5 |
| Insomniac | 2 | 6 | 0 | 6 |
| Pragmata | 3 | 1 | 4 | 5 |
| SuckerPunch | 3 | 2 | 3 | 5 |
| TalesOfArise | 3 | 3 | 2 | 5 |
| UnrealGvas | 3 | 3 | 3 | 6 |
| Witcher3 | 3 | 4 | 1 | 5 |
| XorJson | 4 | 0 | 3 | 3 |

Across **135 demand groups**: **57 enabled**, **38 read-only with evidence** and
**40 not located**, giving **78 gaps**. These counts include candidate representations
and grouped player goals as defined above; they are not 78 implementation-ready tasks.

### AstroPlayroom — ASTRO's PLAYROOM

Baseline: [README](../plugins/AstroPlayroom/README.md#enabled-writes-and-their-evidence).
Collectible and gatcha guides support demand for coins, museum/puzzle ownership and
special bots [S01], [S02]. There is no PC-version mapping assumed.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| AP01 | Coins / gatcha budget | E — NumCoin name V; gameplay range/effect I. Direct coin and bounded budget previews already exist. | — |
| AP02 | Artifact, puzzle-piece and prop ownership | E — I. Existing IsGot fields, collect-existing and candidate-world presets; mixed-event transitions and exact-span tests. | — |
| AP03 | Existing progress / world / cinematic flags | E — I. Individual present booleans; no derived reward repair. | — |
| AP04 | Four special-bot grants / transfer readiness | N — supplied older schema lacks the required bot structures. A guide or a newer text-edit anecdote supplies no compatible target. | Research; H / L / H. B newer-build saves; C separate eligibility/rescue/transfer events and companion state; T. |
| AP05 | Coherent stage completion with rewards and notifications | N — raw flags are AP03, but the complete completion/reward relationship is not located. | Research; H / L / H. C pickup, normal/boss clear, cinematic and notification pairs; B multiple stages; T. |

Integrity: no payload checksum/signature was identified; opaque suffixes and allocation
padding are preserved. Reparse and exact-span preservation do not establish acceptance.
The low-risk remaining convenience is restoring selected ownership to its imported state,
not claiming a complete-world or missing-record grant.

### AtomicHeart — Atomic Heart

Baseline: [README](../plugins/AtomicHeart/README.md#enabled-writes-and-their-evidence).
Crafting/ability guides identify materials, ammunition, healing items, Neuropolymer,
skills and weapon modules as useful targets [S03], [S04]. They do not identify PS5 fields.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| AH01 | Existing material / ammo / consumable quantities | E — I. Explicit populated-slot Amount tags, bounded by the original container maximum; selected/document/query target preview. | — |
| AH02 | Change an existing weapon / ammo asset | E — I. Same-category catalog replacement using an unambiguous full object path already in the save; screenshot-only joins excluded. | — |
| AH03 | Neuropolymer balance | N — a named authoritative currency/resource target is not established by this single snapshot. Do not relabel an arbitrary Amount. | Research; H / M / H. C known gain/spend; B positive balances and NORA UI; T. |
| AH04 | Skill purchase / refund / ranks | R — three saved skill collections located, but sampled sets/maps have zero counts and ownership/rank meanings are U. | Research; H / L / H. B populated collections; C one purchase/refund with costs, prerequisites and related records; T. |
| AH05 | Backpack / storage moves | R — grid identities, ItemId and positions located; read-only. Allocation, ownership and equipment references lack a writer. | Research; H / L / H. B populated storage/empty-slot transitions; C move out/back and capacity boundaries; T. |
| AH06 | Weapon module upgrades / blueprints | N — weapon references and guide module names do not locate upgrade ranks or blueprint acquisition state. | Research; H / L / H. C learn/equip/upgrade separately; B known module/loadout states; T. |
| AH07 | Add items / unlock quests or Testing Ground rewards | N — no complete record constructor or objective/reward state mapping. | Research; M / L / H. C first acquisition, slot reuse and separate objective/reward captures; B late/DLC states; T. |

Integrity: SHA-1 and enclosing lengths are repaired, changed frames use stored Kraken
blocks, and the complete intended plaintext is compared after decode/reparse. Inner
integrity, stored-block acceptance and output capacity remain unverified. Restoring
eligible Amounts to imported values can reuse today's writer without creating slots.

### BaldursGate3 — Baldur's Gate 3

Baseline: [README](../plugins/BaldursGate3/README.md#enabled-writes-and-their-evidence).
Community editors prioritize gold, character progression, abilities, items and spells
[S05], [S06]. Some apparent editor features generate in-game commands; they are not
evidence of a packed-save writer.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| BG01 | Save-menu level / XP / leader display name | E — I. SaveInfo number tokens and metadata name only; no live-character synchronization. | — |
| BG02 | Existing stack amounts / reorder existing container keys | E — U gameplay meaning. Validated NewAge groups and fixed-width contributions; reorder/restore previews already exist. Not a player-gold editor. | — |
| BG03 | Current / temporary HP and effective ability-cache candidates | E — U. Validated PS5 row profiles and conservative ranges; owner/epoch/cache authority unproven. | — |
| BG04 | Live class / level changes | R — class UUIDs and level candidates decoded; reference views remain U. Summary level is BG01. | Research; H / L / H. C single class/level-up with skills, spells and rewards; B multiclass states; T. |
| BG05 | Learned / prepared spells | R — decoded spell-book storage; learned rows empty, prepared entries depend on variable strings/source/owner. | Research; H / L / H. B populated learned and prepared records; C learn/prepare/unprepare and rest; T. |
| BG06 | Item creation / equipment / transfer | R — template, container and item-reference candidates located; unique instance/owner bridge and allocator/cleanup incomplete. | Research; H / L / H. C pickup/equip/move/remove with authoritative owner; B repeated templates and nonempty inventories; T. |
| BG07 | Player gold balance | N — four gold-template occurrences are not an authoritative owned stack; duplicate Creator mappings prevent the bridge. | Research; H / L / H. C exact gain/spend/trade and visible owner; B positive live gold contributions; T. |
| BG08 | Live XP and level-up rewards | N — metadata XP is not live XP; authoritative live progression/reward transaction not established. | Research; H / L / H. C XP gain below/through threshold; B independent party members; T. |
| BG09 | Base ability respec / character appearance | N — effective cache is BG03; base-score layout, invalidation and appearance mutation are not established. | Research; H / L / H. C respec/base-versus-boost and appearance changes separately; B representative character records; T. |
| BG10 | Approval / quests / story choices | N — opaque Osiris and unproven subsystem joins supply no safe named transaction. | Research; H / L / H. C one approval/choice/stage event and reward effects; B corresponding story states; T. |

Integrity: supported LSPK v18 digest (MD5+1) repaired and verified; edited LSF/JSON
resources and package are reparsed. Legacy v15/v16 remain read-only. Inner NewAge
dependencies are unknown. No U-only NewAge gameplay candidate enters the shortlist.

### Cyberpunk2077 — Cyberpunk 2077 gameplay

Baseline: [README](../plugins/Cyberpunk2077/README.md#enabled-writes-and-their-evidence).
CyberCAT documents progression, appearance presets, inventory and facts [S07].
Settings JSON belongs to GenericJson, below.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| CP01 | Money | E — I. Unique container-1 Items.money; fixed quantity patch. | — |
| CP02 | Attributes / unspent attribute, perk, relic points | E — I. Five attributes update matching forced-stat mirrors; point balances independent of spent allocations. | — |
| CP03 | Level / Street Cred / stored XP / thresholds | E — I. Owner-1 proficiency schema, applicable mirrors and bounds; no reward curve synthesis. | — |
| CP04 | Existing ammo / component / consumable stacks | E — I. Reviewed 36-name allowlist and exact quantity-only layout; target/refill previews already exist. Catalog size does not widen this gate. | — |
| CP05 | Existing saved facts | E — U semantics. Value plane writable; hashes/count/order unchanged. Named quest repair is not established. | — |
| CP06 | Perk allocations / coherent build respec | R — stored ranks/schema located; effect packages, capacity and budget mutation absent. | Research; H / L / H. C purchase/refund and derived effects; B nonempty perk/cyberware states; T. |
| CP07 | Item grants / tier / attachments / cyberware loadout | R — full item identities, extended trees, equipment slots and stat modifiers decoded read-only. | Research; H / L / H. B owned implants and duplicate items; C add/equip/upgrade with links, capacity and effect records; T. |
| CP08 | Vehicle / fast-travel unlocks | R — garage lists and one registered travel point located; unlock arrays omitted in sample. | Research; H / L / H. B nonempty acquired lists; C buy/summon/register and callbacks; T. |
| CP09 | V appearance presets | N — no current plugin appearance-field model/writer documented. External presets are PC evidence only. | Research; M / L / H. B matched appearance structures; C one slider change and game re-save; T. |

Integrity: CSAV/LZ4 lengths, pools and offsets rebuilt; intended payload and mirrors
reparsed. No known container checksum; opaque inner checks remain unknown. A reviewed
batch of existing point balances is feasible; a budget-preserving respec is not.

### EldenRing — Elden Ring

Baseline: [README](../plugins/EldenRing/README.md#enabled-writes-and-their-evidence).
ClayAmore's editor demonstrates demand for runes, names, stats, items, equipment,
appearance and world flags [S08]. Only JP has sample coverage here; supported format
150/90 layouts and title registration do not prove every region/build.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| ER01 | Held runes / history / character name | E — I. Mirrored names/history; held <= history; linked rune preview already exists. | — |
| ER02 | Rowa Fruit / Smithing Stone [1] quantities | E — I. Positive ordinary direct-goods rows; observed ceilings 12/3 and refill action. Nonempty storage only synthetically covered. | — |
| ER03 | Other existing inventory quantities | R — inventory row quantities located, but outside the two-ID allowlist or protected by ambiguity/key-item rules. | Conditional expansion; H / M / M. B ordinary positive stacks per ID in held/storage; C consumption, zero behavior and caps; T. No blanket catalog enablement. |
| ER04 | Attributes / respec / HP, FP, stamina | R — separate current/effective/base vital words and archetype byte located. Modifier-aware transaction missing; 504/504/476 HP cannot be collapsed. | Research; H / L / H. C single stat/respec, equipment/boost and damage/heal pairs; B multiple classes/levels; T. |
| ER05 | Equipment / upgrades / affinity / Ashes of War | R — four equipment representations and quick/pouch joins located; profile copies, modifiers and upgrade history unmodeled. | Research; H / L / H. C equip/upgrade/swap-back; B empty/unarmed slots and matched PARAM evidence; T. |
| ER06 | Graces / bosses / quest / NG+ flags | R — bounded event/acquisition candidate ranges and source lookups; U gameplay authority, no dependency writer. | Research; H / L / H. C one named grace/boss/quest event plus no-action controls; B correct event mapping/build; T. |
| ER07 | Full appearance / gender preset | N — no qualified complete appearance setter; the located archetype byte alone is not an appearance model. | Research; M / L / H. B appearance records; C cosmetic change only; T. |

Integrity: source-guided PS wrapper has no PC-style per-slot MD5; original regulation
and unknown bytes preserved, output reparsed. Unknown integrity/dependency warnings
remain. Item insertion/transfer also needs free-handle, map-growth and acquisition work.
ER03 is valuable follow-up research, not an implement-now promise without the per-ID evidence.

### FinalFantasy16 — Final Fantasy XVI retail / demo

Baseline: [README](../plugins/FinalFantasy16/README.md#enabled-writes-and-their-evidence).
The PC table author's features emphasize XP, Gil/AP, quantities, HP and limit break
[S09]. Nenkai documents the save format [S10]; its PC wrapper is not our PS5 container.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| FX01 | Gil / AP / Renown and existing quantities | E — V main-record storage; I named resource joins, U caps/secondary-bank role. Manual and explicit-target main-stack preview; no legal-max preset. | — |
| FX02 | Existing IDs / equipment / shortcuts | E — I for named supported targets, U originals where unmapped. Category/duplicate checks; no ownership reconciliation. | — |
| FX03 | Stored level / XP / HP / recovery / LB / play time | E — I. Gated common prefixes/observed bounds; no stat growth calculation; demo constant LB range offers no alternative. | — |
| FX04 | Raw ranks, flags, commands, summon and party context | E — I/U by field; rank indices and flag effects U. Raw writes do not implement named mastery or travel. | — |
| FX05 | Named skill learn / master / refund | N — rank bytes exist but skill-ID-to-index crosswalk and AP transaction not established. Guide skill IDs do not supply it. | Research; H / L / H. C learn/upgrade/master/reset with AP deltas; B matched skill/growth data; T. |
| FX06 | Potion potency / pouch-capacity activation | N — IDs 506–509 named, but quantities/allocation are not proven activation state. | Research; H / M / H. C each upgrade and both banks/stages; B zero-stack and replay controls; T. |
| FX07 | Add / remove / allocate inventory | N — no complete free-list/generation/reference writer. | Research; H / L / H. C first buy, last sell, reacquire and slot reuse; B allocator boundaries; T. |
| FX08 | Coherent quest / obelisk / completion unlocks | N — context IDs and guide references lack authoritative discovery/reward state. | Research; M / L / H. C one activation/objective/reward separately; B corresponding blocks; T. |

Integrity: known CRC32/ISO-HDLC over 0x10..EOF repaired, container/allocators reparsed;
unknown blocks retained. Named balance previews and restoration of eligible main
quantities can reuse existing setters. They must not claim AP spending/refunds,
Renown reward grants, key-item activation or capacity repair.

### FinalFantasy7 — Remake Intergrade / INTERmission; Rebirth retail / demo

Baseline: [README](../plugins/FinalFantasy7/README.md#enabled-writes-and-their-evidence).
GTC supports both PC families [S11], [S12]; the Rebirth editor author's feature list
also highlights folios, equipment, loadouts, cards, intel and relationships [S13].
INTERmission and demo must retain their distinct curve/schema eligibility.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| FV01 | Gil / consumables / mapped materials, tokens and card counts | E — I. Existing eligible non-equipment counts; restock preview already exists. Cards summary is read-only, underlying eligible counts writable. | — |
| FV02 | Character level / XP / current HP / MP | E — I. Source curves synchronize XP/level; HP/MP bounded by imported current/max. No derived maxima calculation. | — |
| FV03 | Ordinary materia AP / stars | E — I. Coherent ordinary rows and strict curves, dependent AP/star updates and presets. Summons excluded. | — |
| FV04 | Individual mapped progress bits / qualified Rebirth weapon references | E — I. Preserves neighboring bits and unique owned-instance joins. No completion/loadout guarantee. | — |
| FV05 | Stored play time | R — retail mapping V, demo I; u32 seconds in first RESD at 0x2B0 / 0x3C30. | Narrow stored-time setter feasible; M / S / M. C displayed timer and native normalization; T. Preserve older-state copies. Lower value than quantity/recovery workflows. |
| FV06 | Materia sockets / armor / accessory / reusable loadouts | R — owned/detail joins and eight-position reference arrays located; usable capacity, owners and linked-slot transactions unproven. | Research; H / L / H. C slot moves/equip with both owners and linked pairs; B complete capacity state; T. Persistent presets also involve SDK-07. |
| FV07 | Folios / crafting / enemy intel / relationships / minigames | R — source-mapped folio/crafting/minigame bits, intel counters and five relationship words; read-only research values. | Research; H / L / H. C one node, assessment, craft, score, relationship and reward separately; B exact meaning/dependency crosswalks; T. |
| FV08 | Maximum HP / MP and derived growth | R — maxima read as caches; no growth/effect transaction. | Research; M / L / H. C level/equipment changes; B guest/active character identification and curves; T. |
| FV09 | Summon-tier / outfit / complete chapter reward presets | N — ordinary AP/progress-bit writers do not locate the distinct complete state transaction. Summon zero thresholds conflict with observed tiers. | Research; H / L / H. C summon tier with AP unchanged, outfit acquisition and chapter/reset/reward separately; B all variants; T. |

Integrity: directly readable custom linked records; no confirmed checksum to repair.
Demo deltas affect bodies without envelope/tail changes; preserve unknown bytes and
reparse. Recovery and party-level previews can reuse FV02; they must retain the
existing unproven derived-stat/reward and game-rejection warnings.

### Gearbox — Borderlands 3; Tiny Tina's Wonderlands

Baseline: [README](../plugins/Gearbox/README.md#enabled-writes-and-their-evidence).
BL3 and TTWL CLI authors document progression, gear scaling, currencies, SDUs,
unlocks and character customization [S14], [S15], [S16]. BL4 is excluded.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| GB01 | Name / level / XP / unspent skill points | E — V schema meanings. XP curves and supported existing level/point consequences; milestones not synthesized. | — |
| GB02 | Money / eridium or moon orbs / keys / ammo / SDUs / difficulty | E — V legacy currency/ammo schemas; I SDU/Mayhem/Chaos policies and consequences. Title-specific gates. | — |
| GB03 | Item level / rerolls / TTWL Chaos tier / flags / existing part slots | E — I. Fully mapped serial layouts, immutable category/width gates, preserved seed/tail and CRC repair. | — |
| GB04 | Duplicate / import / remove / bank transfers / lost-loot claim | E — I. F5/F6 and explicit F7 member ownership/capacity handling; rejects metadata loss. | — |
| GB05 | BL3 Guardian / TTWL Hero and Myth values | E — I. Existing fields only, through explicit character/profile groups; mirror precedence and budgets still unproven. | — |
| GB06 | Spent skills / class or backstory replacement / coherent respec | R — schema subobjects and stored tree data located, but exact subobject crosswalks/effects/budgets not complete. | Research; H / L / H. C allocate/refund/class change; B complete skill records and account mirrors; T. |
| GB07 | Missions / challenges / cosmetic unlock lists | R — stored mission/checklist/schema representations located; exact identity and dependent unlock/reward writer incomplete. | Research; H / L / H. C one unlock/mission/reward and cosmetic selection separately; B positive lists per title/build; T. |
| GB08 | Arbitrary new item build with validated part compatibility | N — existing serial fields are GB03; a complete legality/effect/part-count construction model is not located. | Research; M / L / H. B pinned pools, exclusions, DLC and complete layouts; C natural gear comparisons; T. |

Integrity: role-specific rolling obfuscation re-encoded; protobuf lengths preserved or
repaired; changed serial folded CRC repaired and rechecked. No additional known wrapper
checksum. Reparse is not item legality validation. New ammo/mark/character-level-derived
gear workflows can compose known setters without extending part compatibility.

### GenericJson — Cyberpunk settings; It Takes Two; Ridge Racer 2 wrapper; generic JSON

Baseline: [README](../plugins/GenericJson/README.md#enabled-writes-and-their-evidence).
John's PC editor and minigame guide establish It Takes Two profile/collection goals
[S17], [S18]. Sony documents Ridge Racer's wrapper video/rewind features [S19].
Settings goals are distinct from Cyberpunk's binary gameplay features [S07].

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| GJ01 | Cyberpunk recorded settings / defaults | E — I. v136 self-described bounds, paired choice/index edits and recorded-default presets. | — |
| GJ02 | Cyberpunk dynamic language / HDR text and explicit index | E — U. Existing imported text length/index only; no fabricated choice list. | — |
| GJ03 | It Takes Two present minigame flags / scores / win counts | E — I. String-preserving adapters, present-flag and counter-zero presets; no missing keys inserted. | — |
| GJ04 | It Takes Two existing chapter / checkpoint tuple | E — I. Three existing unique strings edited together; no story graph validation. | — |
| GJ05 | Ridge Racer existing wrapper primitives / imported-value restore | E — U gameplay interpretation. Wrapper JSON only. | — |
| GJ06 | Generic scalar / syntax structure editing | E — U game semantics; explicit JSON templates and structural undo. Valid JSON is not a game schema. | — |
| GJ07 | Grant missing minigames / complete story unlock preset | N — no legal missing-key/default/dependency model; generic object insertion is GJ06 only. | Research; H / M / H. B fresh/incomplete/complete profiles; C discover/play/chapter/checkpoint; T. |
| GJ08 | Named Ridge Racer filter / upscale / controller preset | N — numeric values exist under GJ05, but title-specific enum-to-menu mapping is not located. | Research; M / M / M. C one wrapper option per pair on the exact PSP wrapper build; B domains; T. Do not import PS1 meanings. |
| GJ09 | Ridge Racer cars / tours / race progress | N — DATA.BIN gameplay is a separate unsupported format. | Research; H / L / H. B gameplay files; C unlock/win pairs and independent format/integrity work; T. |

Integrity: exact JSON token/trivia preservation and reparse; external game integrity
unknown. No proven coupled-file invariant exists. New named presets require actual
definitions, not broad permission to insert JSON. F4/F6 binding delivery is already
resolved; remaining aliases and schemas are plugin/evidence work.

### GodOfWarRagnarok — God of War Ragnarök

Baseline: [README](../plugins/GodOfWarRagnarok/README.md#enabled-writes-and-their-evidence).
The editor author's PC feature list targets resource and gear changes [S20]. Its
claims do not provide a public PS5 integrity algorithm. Only PPSA08330 has samples.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| GW01 | Existing Hacksilver / resources / XP and raw wallet counters | E — I quantity/container roles, U unmapped meanings. Each counter bounded by its own imported value; restore preview already exists. No above-import grant. | — |
| GW02 | Stored play time | E — I seconds. Fixed metadata u32 and reparse. | — |
| GW03 | Individual skill unlocks | R — grouped flags located; four tree group names corroborated, individual skill/bit IDs U. | Research; H / L / H. C one buy/upgrade/refund and XP cost; B positive matched groups and independent integrity evidence; T. |
| GW04 | Gear / runic / enchantment equips and upgrades | N — wallet IDs and read-only guide art do not join directory/flag IDs to owned gear state. | Research; H / L / H. C acquire/equip/upgrade separately; B instance/slot/dependency mapping; T. |
| GW05 | Difficulty | N — imported PS4 offset fails PS5 enum checks in 8/76 records; no named difficulty field. | Research; M / M / H. C menu difficulty changes on matched PS5 builds; B actual field location; T. |
| GW06 | Quest / raven completion with rewards | N — a raw raven counter in GW01 is not pickup or reward state. | Research; H / L / H. C individual pickup, threshold and reward claim; B corresponding flags; T. |
| GW07 | Named preferences / accessibility | N — validated 696-byte blob only; individual option locations/domains unconfirmed. | Research; M / M / H. C one option and native reset; B mappings and integrity; T. |

Integrity: extensive finite checksum/digest searches found no algorithm; **no checksum
is recomputed**. Known grammar is reparsed and unknown bytes retained. This materially
reduces the value/risk ratio of expanding coverage. Neither a wallet ID nor a skill
group label establishes an unlock writer, and this plugin gets no forced shortlist slot.

### Insomniac — Ratchet & Clank: Rift Apart

Baseline: [README](../plugins/Insomniac/README.md#enabled-writes-and-their-evidence).
Weapon and armor guides emphasize bolts, raritanium, weapon leveling, upgrades and
collection bonuses [S21], [S22]. Other Ratchet games' editors are not format evidence;
this plugin does not claim Spider-Man support.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| IN01 | Bolts / calculated balance | E — I. Career/Spent counters and existing balance-target preview; final-pair invariant. | — |
| IN02 | Existing gold-bolt options / boolean preferences | E — I. Seven switches and 61 observed preference booleans; no collectible grants. | — |
| IN03 | Raritanium balance | R — career/spent counters located, but spending exceeds career in later samples; available balance undefined. | Research; H / M / H. C gain/spend with HUD and alternate contributors; B both counters; T. |
| IN04 | Weapon XP / ammo / Omega upgrades | R — per-record candidates located, I schema; ownership, caps and level conversion unproven; no positive Omega sample. | Research; H / L / H. B positive Omega/owned instances; C acquire/XP/ammo/refill separately; T. |
| IN05 | Armor collection / cosmetic appearance | R — collection bits and vanity config/ID/color slots located; I candidate joins, incomplete equip/bonus semantics. | Research; H / L / H. C collect versus equip/color separately; B slot/character joins and color domains; T. |
| IN06 | Raritanium upgrade nodes / build presets | R — Mods/ModGrid/ModCount/SelectedCell masks located, U node costs/prerequisites. | Research; H / L / H. C one purchase/refund with displayed cost; B node identity and effect mappings; T. |
| IN07 | Spybots / gold bolts / world and mission completion | R — checklists, award counters and mission states located; candidate identity/state links incomplete. | Research; H / L / H. C named pickup/objective/reward; B exact location and linked state; T. Guide map pins are not save-bit joins. |
| IN08 | Quick-select loadout | R — four exact weapon paths located; strings/metadata lack a relocation writer. | Research; M / M / H. C swap and swap-back; B encoding/length rules plus ownership/equip joins; T. |

Integrity: DAT1 and supported DDL/inventory reparsed; name CRCs are identifiers, not
body checksums. Body integrity unknown. Despite high demand, writing guessed masks
or subtracting raritanium counters is not an implementable warned scalar feature.

### Pragmata — Pragmata

Baseline: [README](../plugins/Pragmata/README.md#enabled-writes-and-their-evidence).
Upgrade guides prioritize Lunafilament, Pure Lunum, weapons and abilities [S23];
REE Save Editor provides format research, not a complete friendly editor [S24].

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| PR01 | Existing quantities | E — I. Fixed int32 slots and selected/document/query preview; raw IDs remain visible. | — |
| PR02 | Existing IDs / gadget / loadout / progress / quest / world primitives | E — U family/gameplay meanings; named raw fields I identifiers. Fixed-size only, no dependency repairs. | — |
| PR03 | Header / synchronized clocks | E — I named fields, U others. Derived clock changes reported and undoable. | — |
| PR04 | Localized save description / other text | R — system Title/SubTitle/Detail and typed strings located; string/struct resizing read-only. | Research; L / L / H. B alignment/capacity model; C one native text change if supported; T. |
| PR05 | Named Lunafilament / Pure Lunum balances | N — all 90 inventory IDs lack player-facing names; MainSlot is not money. Quantity access PR01 is not a named currency claim. | Research; H / M / H. C isolated gain/spend with UI and exact IDs; B build-matched localization/ID joins; T. |
| PR06 | Named weapon / firmware upgrades and respec | N — hash/schema/enum names do not identify purchase costs, stored upgrade IDs and effects. | Research; H / L / H. C print/upgrade/assign/refund separately; B saved-ID/name and prerequisite joins; T. |
| PR07 | Add / remove inventory or loadout records | N — no rebuilding/allocation writer; fixed-size preservation is insufficient. | Research; H / L / H. B complete typed grammar/alignment; C first pickup, removal and reuse; T. |
| PR08 | Coherent mission / checkpoint / collectible unlocks | N — raw states exist under PR02, but mission conditions and guide atlas lack authoritative state/reward/location joins. | Research; M / L / H. C mission/reward/checkpoint separately; B build-matched mission and hash joins; T. |

Integrity: Mandarin fixed-size encryption path retained; CityHash64 and Murmur3
repaired and output decrypted/reparsed. RSA trailer remains unverified and unchanged;
game rejection warning remains essential. A quantity-restore workflow can retain I
storage evidence without inventing a currency label or legal cap. It must respect
the existing unloaded-binding guard, not assume SDK-02 is fully delivered.

### SuckerPunch — Ghost of Tsushima; Ghost of Yōtei

Baseline: [README](../plugins/SuckerPunch/README.md#enabled-writes-and-their-evidence).
Tsushima technique guidance and Yōtei collection guidance establish progression,
equipment and world goals [S25], [S26]; Yōtei's economy/upgrade guide covers Mon,
Flowers and materials [S27]. These are separate save schemas.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| SP01 | Tsushima Supplies / materials / Flowers / ammo | E — I resources; V paired ammo byte structure. Existing target previews already cover eligible resources/ammo. | — |
| SP02 | Tsushima play time | E — I, v48/v49 only; observed subtitle correlation, raw duration/counter warning. | — |
| SP03 | Yōtei Mon / Flowers / seven ingredients / five ammo types | E — I. Fourteen eligible existing quantity types and a target preview; no locked-record grant. | — |
| SP04 | Tsushima difficulty | R — current string and per-difficulty counters located, I; changing lengths and dependent state lacks a writer. | Research; M / M / H. C difficulty change and same-length controls; B enclosing lengths/counter rules; T. Subtitle ownership is SDK-10, not an automatic repair. |
| SP05 | Equipment / technique references | R — Tsushima identifier occurrences and Yōtei 1,037-name registry located read-only. Ownership/selected state U; registry invariance is not ownership. | Research; H / L / H. C acquire/equip/upgrade; B changing owned-state representations, not more guide names; T. |
| SP06 | Technique points / character stats / health or spirit | N — no authoritative writable progression transaction. A technique name is not a point or rank field. | Research; H / L / H. C earn/spend/heal/stat events separately; B located state and mirrors; T. |
| SP07 | World / quest / collectible completion | N — guide positions and identifier scans do not locate authoritative discovery/reward state. | Research; H / L / H. C one pickup, liberation or quest/reward; B linked flags/counters; T. |
| SP08 | Yōtei play time / active difficulty | N — authoritative timer/difficulty still unknown; Tsushima offsets cannot be reused. | Research; M / M / H. C timed no-action pair and one difficulty change; B Yōtei field/coverage mapping; T. |

Integrity: Tsushima's known additive checksum repaired and output reparsed. Yōtei
recomputes a different-header additive candidate, but tail coverage is unknown;
mismatching candidates stay inspection-only. Restore-to-import ammo/quantity actions
are possible within each existing eligibility gate, with no new cap or unlock claim.

### TalesOfArise — Tales of Arise EU base game

Baseline: [README](../plugins/TalesOfArise/README.md#enabled-writes-and-their-evidence).
The Gald table author and skill-panel guide establish currency and SP/skill goals
[S28], [S29]. No Beyond the Dawn or other-region claim is made.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| TA01 | Gald | E — I. PartyProfile fixed word; all supplied values zero, so nonzero gameplay calibration still missing. | — |
| TA02 | Existing character level / HP / SP | E — I, status blocks 000–005 with supported original values/nonzero level. Does not prove active recruitment or recalculate growth. | — |
| TA03 | Existing consumable quantities | E — I, recognized IDs 1–13, original/target 0–15. Apple Gel observed once; target preview exists. | — |
| TA04 | XP / base HP / coherent growth | R — source-guided body +0x10 / +0x50 words located, I. Growth, effective HP and preview dependencies lack a writer. | Research; H / M / H. C XP below/through threshold and stat changes; B curves and active/guest identities; T. |
| TA05 | Stored play time | R — body seconds/ticks, duplicate tick counter and outer seconds located. Meaning I, mirrors not reconciled. | Research; L / M / M. C timed save/reload pairs to establish authority/rounding; B all copies; T. |
| TA06 | Existing item identity / broader quantities | R — all 934 quantity/flag/ID records located; only 13 IDs eligible. Catalog names are not complete numeric joins. | Conditional expansion; H / M / H. C acquisition/use/zero/caps per ordinary ID; B positive mapped materials and flags; T. No accessory/key-item blanket grant. |
| TA07 | CP / equipment / arte and skill/title purchases | N — authoritative state/identity/dependency joins absent; reference panels and raw sections are not located purchase fields. | Research; H / L / H. C spend/refund/equip/arte events; B complete ID/budget/effect mapping; T. |
| TA08 | Recipes / fish / owls / quest or artifact completion | N — guide records and raw sections lack found/learned/rewarded state joins. | Research; M / L / H. C each event and reward separately; B exact flags/counters; T. |

Integrity: known AES/XOR format re-encrypted, ciphertext SHA-1 recomputed, output
decrypted and plaintext compared. The outer preview is deliberately preserved.
Restoring existing recognized quantities to their own imported values needs no new
item construction or global restore-all/HP/CP transaction.

### UnrealGvas — Hogwarts Legacy; Jedi: Fallen Order; Sackboy; generic GVAS

Baseline: [README](../plugins/UnrealGvas/README.md#enabled-writes-and-their-evidence)
and [Hogwarts evidence](../plugins/UnrealGvas/HOGWARTS.md).
Hogwarts editors document resources, XP and talents [S30], [S31]; EA describes Jedi
skill points, Force and stims [S32]; Sackboy's shop and world tool identify currency,
costume and progression goals [S33], [S34]. The latter tool is older-console evidence
only; a generic type writer does not confer title semantics.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| UG01 | Hogwarts existing currency / XP / level / quantities | E — I only for eligible existing cells in five allowlisted tables and matching column/value types. Knuts rows searchable; no automatic owner or two-image mirror choice. | — |
| UG02 | Hogwarts name / appearance / gear identity replacement | R — identity, item/variation and equipped-slot columns located read-only; no safe replacement/appearance join. Other eligible gear scalar cells remain UG01. | Research; H / L / H. C rename/equip/appearance events; B row ownership, keys and related rows; T. |
| UG03 | Hogwarts talents / collectibles / map / quests | R — PerkDynamic, CollectionDynamic, MissionDynamic and map tables located; read-only, authority/rewards U. A generic MiscData value is not a talent transaction. | Research; H / L / H. C spend talent, collect, discover and claim rewards separately; B duplicate-row identities and image roles; T. |
| UG04 | Jedi existing tagged gameplay scalars / settings | E — I gameplay meanings; V generic storage. Raw scalar writer; Crc32, compressed actor data and paired length excluded. | — |
| UG05 | Jedi named XP / skills / HP / Force / stim gameplay controls | N — no authoritative named target/dependency model documented; decoded actor tags/string observations are inspection only. | Research; H / L / H. C XP/spend/damage/stim/rest separately; B complete actor schema and checksum coverage; T. |
| UG06 | Jedi costume / lightsaber / exploration grants | N — observed strings do not establish ownership/unlock fields. | Research; M / L / H. C one cosmetic or discovery; B exact records and actor writer; T. |
| UG07 | Sackboy Collectabells / existing supported General scalars | E — I. Includes present progression scalars where parsed; no coherent unlock-all promise. | — |
| UG08 | Sackboy costume / emote state | R — CostumeSaveData, unlockedEmotes and SavedBoundEmotes representations located; named projections read-only, complete item/state writer absent. Parsed primitive leaves may already be raw-editable under UG07. | Research; M / L / H. C buy/equip/bind separately; B complex collection identities/defaults and checksum; T. |
| UG09 | Semantic new-item / missing-unlock construction in generic GVAS | N — no title-specific constructor/dependency contract. Existing primitive remove-last is syntax-level only. | Research; M / L / H. B title schema/defaults/identities; C acquisition/removal; T. |

Integrity: GVAS sizes and string/container lengths repaired. Hogwarts validates both
independent SQLite images, rebuilds changed arrays with stored Kraken, and checks
SQLite integrity again; 16 MiB output ceiling is not a console quota guarantee.
Wrapped/partial cases stay read-only. Jedi/Sackboy checksum coverage is unsolved;
existing writes retain rejection warnings. Do not copy both databases indiscriminately
or turn a raw State cell into a completion promise. No new title-specific preset is
shortlisted without an authoritative target beyond a column-name heuristic.

### Witcher3 — The Witcher 3: Wild Hunt – Complete Edition

Baseline: [README](../plugins/Witcher3/README.md#enabled-writes-and-their-evidence).
The save-editor author's feature list establishes level/skills, inventory/Crowns,
Gwent, recipes, maps and story-choice goals [S35]. Its PC coverage is not PS5 support.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| WI01 | Level / XP / free points / Crowns / quantities / durability | E — I. Unique player IdTag, supported SAV3 64/23/163 and preserving writer; repair and ordinary Bread/water refill previews exist. | — |
| WI02 | Existing non-core skill ranks | E — I. Bound by saved maxLevel; no purchase/refund/effect/slot respec. | — |
| WI03 | Existing Gwent ordinary / leader counts | E — I. Typed existing index/count pairs; deck bytes unchanged; may invalidate decks. | — |
| WI04 | Equipment / stash / Roach transfer | R — inventory identity/flags/slot encodings located; allocation/reference/ownership cleanup writer incomplete. | Research; H / L / H. B stash/Roach/duplicate instances; C equip/transfer/back; T. |
| WI05 | Recipes / crafting grants | R — read-only saved recipe representation and catalog references; grant dependencies unmodeled. | Research; M / M / H. C learn then craft; B required identities and facts; T. |
| WI06 | Facts / journal / map discovery and story choices | R — read-only raw facts, journal paths and native tags; names/region/state joins incomplete. | Research; H / L / H. C one choice/discovery/objective and downstream effects; B authoritative crosswalk; T. |
| WI07 | Spent points / mutations / coherent respec | R — used/free point arrays and saved ability/slot candidates located; mutation/effect/prerequisite transaction absent. | Research; H / L / H. C purchase/refund/mutation/slot separately; B budgets, effects and capacity; T. |
| WI08 | Character appearance | N — no qualified appearance mapping/writer documented. | Research; L / L / H. B matched appearance record; C one native cosmetic change; T. |

Integrity: researched SNFH/SAV3 writer has no known content checksum; LZ4 framing,
lengths and relocated pointers repaired and output reparsed. Unknown internal checks
and later schemas remain caveats. A reviewed batch of existing card counts can reuse
WI03; it cannot add missing indices, repair decks or claim collection rewards.

### XorJson — Minecraft Dungeons II; generic transformed JSON

Baseline: [README](../plugins/XorJson/README.md#enabled-writes-and-their-evidence).
The community editor author targets hero stats, gear/effects, quests and map shortcuts
[S36]. Dungeons II evidence is used; Minecraft Dungeons I data is not substituted.

| ID | Wanted edit | State, confidence and current evidence | Gap feasibility; value / effort / risk; evidence needed |
| --- | --- | --- | --- |
| XJ01 | Existing hero attributes / currency or XP tokens | E — I matching catalog policy, U uncalibrated gameplay semantics. Raw exact tokens and stored-attribute preview; not a coherent level-up writer. | — |
| XJ02 | Existing stack / ID / rarity / gear scalar values | E — I catalog actions, U other raw meanings. Existing tokens only; skips merchant/special rows and preserves -1 stack sentinel. | — |
| XJ03 | Existing quest / world / achievement / collection tokens | E — U effects and reward relationships. Raw writable state is not a verified finish-quest or explore-map preset. | — |
| XJ04 | Settings / generic JSON scalar and structure edits | E — U scalar effects, I structural/bulk behavior. Shifted global settings; F5 and explicit F7 groups; one supplied generation. | — |
| XJ05 | Semantic item creation / equip / effect or enchantment synthesis | N — generic JSON insertion and known catalog IDs do not establish required generator/effect/slot dependencies. | Research; H / L / H. B positive effect/enchantment/talisman and duplicate-seed records; C pickup/equip/effect changes; T. |
| XJ06 | Coherent level / quest rewards / map unlock presets | N — complete PS5 state/budget transaction unlocated; only 2/49 quest IDs match the PC catalog. | Research; H / L / H. C progression, reward, station and door events; B exact PS5 identity/PC crosswalk; T. |
| XJ07 | Named appearance preset | N — no qualified PS5 appearance mapping in the current plugin evidence. | Research; M / L / H. B appearance structures; C one native cosmetic change; T. |

Integrity: plain character JSON and byte-minus-one global JSON, not XOR-1 for the
sample. No decoded checksum observed; external integrity/dependencies U. Preserve
exact tokens and reparse, with the offline SoftVersion 2 / FormatHash 3777917472 gate.
Online/unknown modes and other versions stay read-only. A PC feature claim cannot
upgrade these U gameplay joins into shortlist-ready evidence.

## Ranked top 15 additions

The order favors useful, narrowly bounded work with known targets and no new opaque
dependency transaction. **Fourteen are new reviewed workflows over already enabled
fields; one is a new stored-field setter.** They are not fifteen newly discovered
gameplay fields. Raw exposure is already extensive; inventing fifteen new field
meanings would contradict the evidence above. Workflow gaps are separate from R/N
field-demand counts. No source claims measured popularity for these exact presets.

All candidates are implementable with current V/I evidence; C/T validate their effects
and narrow warnings, rather than gate their initial Experimental implementation.
None requires U gameplay targets, expanding an allowlist/cap, synthesizing missing
records or resolving an open SDK request. Effort includes meaningful byte/reopen,
atomicity and history validation. Reuse F6 pure preview/revalidation, complete target
resolution, explicit exclusions/skips and one-step undo. Never replace an existing
setter's eligibility/range policy with catalog metadata.

“Restore imported” means the immutable opening value, not a game maximum or the
value before the last edit. It is useful after staging reductions in this document;
it cannot recover a historical balance lost before import. Re-imported edited saves
establish a new baseline. Such actions preserve values already above that baseline
where described; they are not gameplay replenishment from earlier saves.

| Rank | Addition / exact new behavior | Evidence and boundary | Value / effort / incremental risk | Remaining owner evidence and retained warning |
| ---: | --- | --- | --- | --- |
| 1 | **FF7: recover selected characters' current HP/MP** in one preview, using each stored maximum when it is above current. | FV02, I; existing setters already accept these targets. Skip unsupported curves/records, never lower an over-max current value or change maxima/XP/equipment. New batch workflow. | H / S / L | C damage/heal/MP-use for each variant/active character; T battle/resave. Keep unknown-integrity, derived-stat and console warnings. |
| 2 | **Gearbox: match selected gear levels to the current character level**, clipped by existing title policy; list unsupported serials as skips. | GB01 V character level + GB03 I serial level. Existing action accepts a manual target; derive it from this character only. No rarity/parts/stat synthesis, no automatic profile-wide application. | H / S / L | C known owned gear/card before/after and character-level relation; T equip/use. Retain item-legality and console warnings, repair serial CRC. |
| 3 | **Gearbox: restore supported ammo pools to opening amounts**, only when lower. | GB02 V schema. Existing finite editable pool paths and bounds; no SDU/capacity change or missing-pool creation. New multi-pool preview. | H / S / L | C firing/reload/native refill and visible reserves; T capacity/clamping. Preserve signed-zero no-op behavior and rejection warnings. |
| 4 | **FF16: separate named Gil/AP/Renown balance previews**, each an explicit target for one unambiguous existing main-bank resource row. | FX01 V storage/I name. Reuse conservative F6 quantity ceiling; reject missing/duplicate or unqualified joins. This adds discoverable resource controls to the generic quantity action, not a second binding or reward writer. | H / S / L | C exact gain/spend per resource, adult/stage context; T use/resave. Retain resource-specific dependency/cap warnings; show repaired CRC. |
| 5 | **Tales of Arise: restore eligible existing consumables to opening quantity**, only when lower. | TA03 I; IDs 1–13 and both imported/current quantities within 0–15. No new rows, HP/CP restore or key-item grants. New action rather than another manual target. | M / S / L | C buy/use/zero/reacquisition per ID; T consume after edit. Retain inferred mapping/cap warnings; existing cipher/SHA-1 self-check. |
| 6 | **Atomic Heart: restore selected populated Amounts to opening values**, only when lower and still eligible. | AH01 I; retain container/imported ceilings, signed type and duplicate-container guards. New baseline-target action; no empty-slot population or asset replacement. | M / S / L | C resource/ammo/heal-item use and native refill; T stored-block acceptance/output capacity. Keep inner-integrity, storage-growth and console warnings. |
| 7 | **Tsushima: restore eligible ammo to opening counts**, only when lower, updating both count copies. | SP01 V byte structure; retain existing flags/version/action and 0–32767 gates. New per-row baseline action instead of one shared target; no capacity grant. | M / S / L | C fire/pickup/reload; T multiple ammo kinds. Preview both copies/checksum and retain clamping/console warnings. |
| 8 | **FF7: batch selected character level targets with derived XP** through existing variant/character curves. | FV02 I. Existing direct levels remain unchanged; new per-character preview and one history step. Skip invalid/guest-unqualified rows, preserve original-pair undo; do not claim recalculated maxima or rewards. | H / M / M | C one level-up separately for Remake, INTERmission, retail/demo; T battle/abilities/resave. Preserve unknown growth/reward/integrity warnings. |
| 9 | **Cyberpunk: review a batch of explicit unspent attribute/perk/relic point targets** with one apply/undo. | CP02 I. Existing three balance setters and bounds; omitted-property rebuilding already supported. No spent-point refund, perk purchase, attribute change or budget arithmetic. | H / S / M | C earn/spend each point type; T menus, actual spend and reload. Retain opaque-integrity and independent-budget warnings. |
| 10 | **Gearbox: batch favorite/junk marking of selected existing items** with explicit old/new flags. | GB03 I. Use existing recognized flag setter; skip conflicting favorite+junk/unsupported rows and preserve unknown bits. No item removal/sale, serial rebuild or new instance. | M / S / L | C mark/unmark and sale-protection behavior per title; T UI/resave. Keep console warnings and exact unknown-bit preservation. |
| 11 | **Witcher 3: set selected existing ordinary Gwent card counts** in a reviewed batch. | WI03 I. Existing ordinary-count bounds; no new cardIndex, leaders, deck edit or collection-completion preset. New selection/query workflow over current fields. | H / M / M | C ordinary copy gain/removal; T collection and deck/gameplay/quest behavior. Keep deck-invalidity and reward/dependency warnings. |
| 12 | **FF16: restore selected positive named ordinary main-bank stacks to opening quantities**, only when lower. | FX01 V storage/I labels. Exclude currencies, unique/key/progression/upgrade items, unmapped IDs, secondary bank and ambiguous keys. Narrow restoration policy; no legal-max claim. | M / S / L | C ordinary consumable/material use and bank authority; T use/sell/resave. Retain category/dependency warnings, show CRC. |
| 13 | **ASTRO: restore selected existing IsGot ownership booleans to opening state** with named changes/skips. | AP02 I. Same schema-qualified fields; no insertion, cinematic/notification/reward changes or guaranteed complete-world reversal. New restore workflow alongside collect-existing. | M / S / M | C one pickup and independent ownership reversal; T museum/puzzle/reward/forward-play checks. Keep unknown-integrity and linked-state warnings. |
| 14 | **Pragmata: restore selected eligible existing nonnegative quantities to opening values**, only when lower. | PR01 I quantity storage, raw IDs retained; reject ambiguous/duplicate targets and retain unloaded-binding guard. Use bounded F8-resolved selection; no U named resource or mission preset. | M / M / M | C identify/use the selected stack; T load/equipped/unlock side effects. Keep RSA/console/dependency warnings and existing hash/encryption checks. |
| 15 | **FF7: edit stored first-record play-time seconds** within a conservative observed bound retaining the original. | FV05 V retail mapping/I demo. New fixed-u32 setter; scope is stored time only, not historic records or all menu/play counters. Existing byte-preserving writer/reparse is sufficient. | M / S / M | C displayed time, timed no-action pairs per variant; T native resave/normalization. Keep unknown-integrity/console warning and explicit stored-time limitation. |

The lower incremental risk of a workflow does not remove its parent plugin's unknown
integrity or game-dependency risk. The two last restoration candidates in particular
offer limited benefit outside a staged editing session; they rank below gameplay
recovery and established-schema balance workflows. Yōtei quantity restoration is
also feasible with I storage, but its unresolved checksum-tail coverage and small
increment over the existing target action put it below these fifteen. No per-plugin
quota is used to force low-evidence proposals into the shortlist.

### High-value work deliberately not shortlisted

BG3 live gold/base-score/spell edits, Elden Ring equipment/respec, Rift Apart
raritanium/upgrades, Hogwarts talent/collection grants, and Ragnarök gear/skills have
high player value. Their blockers are concrete missing joins or dependency writers,
not a lack of warning text or permission. Obtain the B/C evidence listed in their
rows, then reassess. Do not rank a PC offset port as ready work. Similarly, Tales CP,
Pragmata named currency IDs and Yōtei technique/health/world state require actual
field discovery. Generic JSON structure editing is not evidence of those semantics.

F5/F6/F7/F8 are already available. [SDK_REQUESTS.md](SDK_REQUESTS.md) identifies
remaining metadata/review/preset conveniences, but none can establish the missing
game facts. In particular, storage registration does not publish all field specs,
document groups do not establish a coupled-file invariant, and a catalog name or
map point does not prove saved ownership/completion. Shortlisted work can keep
existing guards without waiting for new framework contracts.

## Sources

Access dates below are **2026-10-29**. Search-indexed results and readable author
pages were used; no binary/editor was downloaded or run. Sources establish demand
or explicitly identified research leads, not a claim of PS5 compatibility. Current
implementation and integrity evidence is attributed to the local plugin README in
each section. Its upstream algorithm/source pins remain the authority for that
reported evidence; this report does not claim to revalidate every upstream algorithm.

| ID | Author / source and URL | Access date | What it supports / limits |
| --- | --- | --- | --- |
| S01 | Gage / PowerPyx, [ASTRO collectible guide][S01] | 2026-10-29 | Museum/puzzle/gatcha goals and coin spending; no save-ID or checksum proof. |
| S02 | GamesRadar+, [ASTRO special bots guide][S02] | 2026-10-29 | Four-bot discovery/transfer goal; no older-schema insertion method. |
| S03 | Bilawal Bashir / WhatIfGaming, [Atomic Heart crafting][S03] | 2026-10-29 | Resource, ammo, healing and module/weapon crafting goals. |
| S04 | Md Obyead / GameRiv, [Atomic Heart abilities][S04] | 2026-10-29 | Skill branches and upgrades; no PS5 state mapping. |
| S05 | Hawk-on, [BG3 Save Editor][S05] | 2026-10-29 | Gold-editor goal; resource/metadata claims need comparison with live state. |
| S06 | Serofix-code, [BG3 Local Save Editor][S06] | 2026-10-29 | Party/ability/item/spell goals; distinguishes command-generation from packed-save edits. |
| S07 | CDPR Modding Documentation contributors, [CyberCAT guide][S07] | 2026-10-29 | Stats, appearance, inventory/facts workflows and version/dependency caveats. |
| S08 | ClayAmore, [ER Save Editor][S08] | 2026-10-29 | Names/stats/runes/items/equipment/world flags and gender; platform claims are upstream's. |
| S09 | bbfox, [Final Fantasy XVI table][S09] | 2026-10-29 | XP, Gil/AP, counts, HP/LB demand; runtime addresses are not save offsets. |
| S10 | Nenkai, [FFXVI save notes][S10] | 2026-10-29 | Format/tool research lead; PC wrapper not ported to PS5 by assumption. |
| S11 | RyudoSynbios / Game Tools Collection, [Remake editor][S11] | 2026-10-29 | PC Remake/INTERmission file-family support; dynamic fields not inferred from landing page. |
| S12 | RyudoSynbios / Game Tools Collection, [Rebirth editor][S12] | 2026-10-29 | PC Rebirth support; local README's pinned template supplies reported field evidence. |
| S13 | Rebirth editor author / Nexus Mods, [Materia Loadouts / Save Editor][S13] | 2026-10-29 | Party, inventory, materia/folios, intel, relationships, loadout feature goals. |
| S14 | apocalyptech, [BL3 CLI editor][S14] | 2026-10-29 | Progression, SDUs, gear level, cosmetics/unlocks; explicitly PC-tested only. |
| S15 | abramhindle and contributors, [TTWL CLI editor][S15] | 2026-10-29 | Hero/Myth stats, gear levels, Chaos and unlock goals; PC evidence. |
| S16 | Same contributors, [TTWL save-editing guide][S16] | 2026-10-29 | Hero stats, currencies and customization/backstory goals; not our PS5 bounds. |
| S17 | John, [It Takes Two save editor][S17] | 2026-10-29 | PC JSON editor precedent; no assertion of a PS5 story-dependency writer. |
| S18 | PowerPyx, [It Takes Two minigames][S18] | 2026-10-29 | Discovery/replay and score goals, distinct from story progress. |
| S19 | Bandai Namco / PlayStation Store, [Ridge Racer 2][S19] | 2026-10-29 | PSP wrapper filters/rewind/save features; linked regional listing is not evidence for another title ID's enum values. |
| S20 | KG971 / Codes Prince, [Ragnarök PC editor][S20] | 2026-10-29 | Resource/gear edit goals and version restrictions; no public integrity implementation obtained. |
| S21 | Epic Games Store, [Rift Apart weapons guide][S21] | 2026-10-29 | Weapon progress, bolts and raritanium upgrade goals. |
| S22 | Push Square, [Rift Apart armor guide][S22] | 2026-10-29 | Armor collection/bonus goals; no saved-bit/equip mapping. |
| S23 | Ali Hashmi / Prima Games, [Pragmata weapon upgrades][S23] | 2026-10-29 | Lunafilament/Pure Lunum, printing and upgrade goals. |
| S24 | kvasszn and contributors, [REE Save Editor][S24] | 2026-10-29 | Pragmata format/editor context; other games' mappings excluded. |
| S25 | Robert Ramsey / Push Square, [Tsushima techniques][S25] | 2026-10-29 | Legend/technique-point and skill goals. |
| S26 | PowerPyx, [Yōtei collectibles][S26] | 2026-10-29 | Weapon/armor, activities and discovery goals; no save ordering assumption. |
| S27 | All Things How, [Yōtei navigation and upgrades][S27] | 2026-10-29 | Mon/Flowers/material, equipment and upgrade goals; guide evidence only. |
| S28 | EnvyOfMen / Nexus Mods, [Arise Gald Editor][S28] | 2026-10-29 | Currency demand; explicitly a PC memory table. |
| S29 | PowerPyx, [Arise skills guide][S29] | 2026-10-29 | Title-panel and SP purchase goals; not stored node identities. |
| S30 | falker47 / HLSGE contributors, [Hogwarts editor][S30] | 2026-10-29 | Guarded resource/XP/talent and collection goals; its limitations do not become our guarantees. |
| S31 | jianastrero, [Hogwarts save editor][S31] | 2026-10-29 | Currency, XP, talent, inventory goals; planned features distinguished from implemented. |
| S32 | Electronic Arts, [Jedi text manual][S32] | 2026-10-29 | XP/skills, Force/life/stims and rest behavior; no PS5 field layout. |
| S33 | Imagisphere contributors, [Zom Zom's Shop][S33] | 2026-10-29 | Collectabells, costumes, emotes and wardrobe goals. |
| S34 | MsKrypt1, [Sackboy World Unlocker][S34] | 2026-10-29 | World-progression repair goal; older-console tool, not PS5 checksum proof. |
| S35 | techwizop / Nexus Mods, [W3 Save Editor][S35] | 2026-10-29 | Character, inventory, Gwent, recipes, map and story features; PC-only author claim. |
| S36 | Ishiaki and contributors, [Dungeons II editor][S36] | 2026-10-29 | Stats/items/effects/quests/world goals; current PC author claims do not upgrade the pinned PS5 baseline. |

[S01]: https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/
[S02]: https://www.gamesradar.com/games/platformer/astros-playroom-special-bots-locations/
[S03]: https://whatifgaming.com/atomic-heart-crafting-guide/
[S04]: https://gameriv.com/all-abilities-atomic-heart/
[S05]: https://github.com/Hawk-on/bg3-save-editor
[S06]: https://serofix-code.github.io/bg3-save-editor/
[S07]: https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/blob/main/for-mod-creators-theory/modding-tools/savegame-editor-cybercat.md
[S08]: https://github.com/ClayAmore/ER-Save-Editor
[S09]: https://opencheattables.com/viewtopic.php?t=1129
[S10]: https://nenkai.github.io/ffxvi-modding/resources/other/save_files/
[S11]: https://game-tools-collection.com/final-fantasy-vii-remake/save-editor
[S12]: https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor
[S13]: https://www.nexusmods.com/finalfantasy7rebirth/mods/2072
[S14]: https://github.com/apocalyptech/bl3-cli-saveedit
[S15]: https://github.com/abramhindle/ttwl-cli-saveedit/
[S16]: https://github.com/abramhindle/ttwl-cli-saveedit/blob/master/README-saves.md
[S17]: https://johnssaveeditors.com/games/it-takes-two/
[S18]: https://www.powerpyx.com/it-takes-two-all-minigame-locations/
[S19]: https://store.playstation.com/en-us/product/UP0700-PPSA07695_00-RIDGERACER200000
[S20]: https://codesprince.com/2024/09/24/pc-editor-for-gow-ragnarok/
[S21]: https://store.epicgames.com/news/save-the-galaxy-with-our-ratchet-clank-rift-apart-weapons-guide?lang=en-US
[S22]: https://www.pushsquare.com/guides/ratchet-and-clank-rift-apart-all-armour-locations
[S23]: https://primagames.com/tips/how-to-upgrade-weapons-in-pragmata
[S24]: https://github.com/kvasszn/ree-save-editor
[S25]: https://www.pushsquare.com/guides/ghost-of-tsushima-all-techniques-and-best-techniques-to-learn
[S26]: https://www.powerpyx.com/ghost-of-yotei-all-collectibles-locations-guide/
[S27]: https://allthings.how/ghost-of-yoteis-golden-ginkgo-navigation-upgrades-and-early-unlocks/
[S28]: https://www.nexusmods.com/talesofarise/mods/407
[S29]: https://www.powerpyx.com/tales-of-arise-how-to-unlock-all-skills/
[S30]: https://github.com/falker47/HogwartsLegacy-SaveEditor
[S31]: https://github.com/jianastrero/hogwarts-legacy-save-editor
[S32]: https://www.ea.com/able/resources/star-wars/jedi-fallen-order/ps4/text-manual
[S33]: https://wiki.imagisphere.me/Zom_Zom%27s_Shop_%28Sackboy%3A_A_Big_Adventure%29
[S34]: https://github.com/MsKrypt1/SackboyWorldUnlocker
[S35]: https://www.nexusmods.com/witcher3/mods/14414
[S36]: https://github.com/IshiakiZ/mcd2-save-editor/blob/main/README.md

## Validation and handoff

`python docs/tests/test_documentation.py` **passed: 14 tests, 0 failures**.
Additional report checks reconciled all 17 plugin counts, 135 unique demand IDs,
15 consecutive shortlist ranks, 36 external source references and local file links.
`git diff --check` passed. Scratch/check scripts were kept under
`D:/Temp/saveeditor/test-scratch/edit-coverage`.

Only this report is changed; no runtime tests, new sample measurements or console
acceptance are claimed. Implementation candidates need their own focused tests when
implemented. The #40 handoff comment carries this ranked shortlist and distinguishes
new workflows from new field coverage; #31 remains the owner-validation tracker.
