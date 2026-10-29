# FinalFantasy7 completion audit

2026-10-28, starting `main` = `1f0d1f1`; branch `plugin/finalfantasy7-completion`.
Compared every requirement, acceptance criterion and #16 handoff against that baseline.
The owner's write-enable directive overrides evidence-only write gates in the original plan;
newly researched read-only candidates do not constitute complete purchase/reward writers.
All enabled writes remain Experimental/Inferred with unknown-integrity and console warnings.
The disposition and evidence tables below include the later asset and F12/F13 deliveries, checked
against current code during the 2026-10-29 maintainability review. Original validation counts remain
historical; current suite results are recorded in README.

## Requirement disposition

| Requirement | Result / remaining classification |
| --- | --- |
| R1 | Owner-blocked: controlled same-slot purchase/use, XP, AP and gear/materia captures for each variant, #31. |
| R2 | Owner-blocked: console load/effect/battle/resave/reimport, separately by variant, #31. |
| R3 | Done for known names/categories/AP/XP and conservative editor policies. Owner-blocked for verified stack caps, summon exceptions and complete gear capacity/eligibility. |
| R4 | ByteDiffer/relationship/write-scope tests, shared F1 manifest/verifier and SDK-only character evidence hook; F12 decoder and complete F8 traversal tests are implemented. Private capture fixtures cannot be fabricated. |
| R5 | F2 context/packs/Sources and document asset forwarding; bundled attributed icons/maps and optional imported image routing/provenance are tested. |
| R6 | Candidate character sheets shipped; owner-blocked for observed identity, guest/past/live classification and summon screenshots. |
| R7 | F8 complete fixed-slot/catalog/owned/card/progress search/filter/sort/paging. Added 388-row research collection. |
| R8 | F4 materia picker eligibility plus F2 imported names/assets. Weapon type catalog is not an instance-ID catalog; raw IDs retain their domains. |
| R9 | F3 confidence/warnings and row-detail sections/images with text fallback and asset-route tests. F13 browse-only inventory/materia type fields remain; retained legacy owned-page endpoints also bind weapon/materia type fields. Main Owned items F8 type cells are text after the performance round. Writable weapon instance IDs retain their separate domain. |
| R10 | Added F9 Rebirth weapon/armor materia reference inspection with local field targets. Owner-blocked for usable capacities, links, complete moves/swaps and Remake upgrade-node topology. |
| R11 | Existing final-state staging and exact original restoration; scalar/F6 undo/redo remains byte-exact. No safe structural writer exists. |
| R12 | 145 card types / 16 separate packs and numbering anomaly warning shipped. Owner-blocked for verified 012/013 crosswalk, decks/rank and acquisition dependencies. |
| R13 | F6 AP, new star-tier and restock previews show primary/derived changes, exclusions and warnings. |
| R14 | Core F6 revision/ownership/query-scope application with one history group; tests cover stale/tampered rejection and lazy bindings. |
| R15 | Existing framework changes panel and F6 preview; imported name provenance also travels in preview warnings. F9 status refresh tested through edit/undo/redo. |
| R16 | Existing unique-family Rebirth weapon-reference writer retained. Owner-blocked for full gear/armor/accessory/socket and proficiency/folio dependencies. |
| R17 | Existing corruption, AP/XP boundary, alias and ownership tests retained; added F9 validity/targets/state, F6 preset, duplicate detail and sample-backed research tests. |
| R18 | F9 read-only reference layout added; no invented links/capacity. Actual upgrade graph remains owner-blocked because neither inspected MIT template nor KSE library supplies proven PS5 node identities/topology/SP effects. |
| R19 | Won't do inside plugin: external toolkit-owned persistent preset storage is a framework responsibility. Conditional SDK request documented; useful loadout persistence also needs a complete compatible loadout writer. |
| R20 | Resolved: attributed publisher/community assets are permitted under owner decision 2026-10-29; selected icons and six regional backgrounds are bundled. Coordinates remain unverified. |
| R21 | MIT metadata packs, attributed icons, selected official descriptions and imported image routing implemented. Complete per-build/language effect text/art and save-ID/text-key bridges remain incomplete. No NC implementation bundled. |
| R22 | Added read-only folio/crafting/minigame candidates, named enemy records, five relationship raw words and one craftsmanship raw word (source level/XP share an ambiguous address). Owner-blocked for complete costs/prerequisites/reward/actor/counter relationships and verified effects; candidate reads do not prove those dependencies. |
| R23 | Owner-blocked: acquisition/discard/full-capacity and new-patch/region fixtures; no unsafe allocation/removal/conversion. |
| R24 | Six bounded F9 maps, separate state-domain markers, text alternatives, local field navigation and attributed external geography link. Six attributed PowerPyx regional backgrounds are bundled; candidate coordinates remain unavailable/list-only. |
| R25 | Conditional N/A: no proven operation couples gameplay/common files. Owner captures must establish a specific coupling before F7 grouping is meaningful. |

## Acceptance criteria, in plan order

| # | Result |
| --- | --- |
| 1 plugin-only scope, no private data/I/O | Done; source changes confined to plugin, downloads in TEMP, no runtime I/O. |
| 2 source/evidence/identity/confidence | Done for shipped subset; README/sidecars/UI cite pinned metadata and distinguish type, instance, display number and raw byte. Semantic/console confirmation stays #31. |
| 3 every payload lossless | Tests cover all 55 archived payload entries including backups/generations/common/tails; no normalization. |
| 4 byte-scoped atomic edits | Existing scalar/dependency tests plus restock golden ranges, malformed scopes/plans and duplicate-detail preservation. |
| 5 ordinary AP thresholds / summons | Existing below/at/above and direct-star tests retained; F6 star preset uses strict ordinary curves and excludes summons/mismatches. |
| 6 observed character identity | Candidate values implemented; owner-observed identity/effects for all variants remain #31. |
| 7 compatible complete equipment/loadouts | Unique-family weapon reference subset implemented. Full capacity/links/owner model and loadout operations remain owner-blocked. |
| 8 whole-data search / bounds | F8 plus lazy F9; existing initial-view/open budgets retained. Raw/header structural views remain bounded. |
| 9 reviewed card crosswalk / separate domains | Source anomaly explicitly identified; types/packs/opponents separate. Corrected menu crosswalk/decks/rank remain #31. |
| 10 bulk review/cancel/apply/undo | AP/stars/restock pure preview, scoped skipped rows/warnings, stale rejection and exact one-group history tested. |
| 11 conformance / meaningful tests | Full sample-enabled plugin run and zero-warning API build; counts in README and #16 final handoff. |
| 12 offline optional attribution | F2 packs, source sidecars, notices, UI provenance, bundled icons/map backgrounds and optional imported images; external guide supplements list-only candidate positions. |
| 13 progress gating | Original criterion superseded by owner write-enable directive. Existing candidate-bit writes warn about unchanged rewards/actors/counters and softlocks; new research modules have no complete writer. No placeholder aliases treated as separate verified objectives. |
| 14 each PS5 gameplay validation / Stable | Owner-blocked #31. Experimental maturity unchanged. |
| 15 documentation | README updated for actual writes/presets, F1–F13 adoption, research limits, sources and SDK requests. |

## Evidence ledger additions

All mapped additions read the first/current RESD (record 0 at absolute offset 0), so absolute and
record-relative offsets coincide. They never modify previous-state copies, common files or padding.

| Data | Width / identity / capacity | Dependencies / confidence |
| --- | --- | --- |
| Folios | u8 raw bytes and literal bit 0–7; unique `(offset, bit)` IDs; seven source character groups / 238 visible flags | Global addresses; no outer character shift. Read-only/Inferred. Costs/prerequisites/purchase writer unknown. |
| Crafting / minigames | u8 raw bytes and bit candidates from sidecar JSON; 220 / 168 unique flags | Read-only/Inferred; unlock/done/reward domains separated. Ingredients, rewards, scores and actor state unknown. |
| Weapon ref | u32 instance at `0xCC + character*0x100`; seven displayed character records | Existing qualified weapon edit only; read-only inspection alias. Raw values, not type catalog keys. |
| Weapon / armor materia positions | u32 instance at `0xF4` / `0xAC + character*0x100 + slot*4`; eight stored positions each | `0xFFFFFFFF` shown literally as empty sentinel. Names only from unique details. Usable capacity/links/owner repair unknown; no move writer. |
| Materia detail lookup | instance/type at `0xBFA68/+4`, stride `0x20`, 1,000 records | Duplicate instances are ambiguous, never silently overwritten. Read-only inspection; save bytes unchanged. |
| Enemy intel | Source-indexed records: Remake `0x42FAC + index*8`, Rebirth/demo `0x450D0 + index*16`; defeated +4 is u32 / u16 respectively; entry/assessed/staggered bits 0/1/5 | 161 / 230 named rows; read-only/Inferred. Display numbers are never used as array indices. |
| Relationship / craftsmanship | Rebirth relationships at `0xA8 + character*0x100`, source characters 1–5; craftsmanship raw u32 at `0x3DB8` | Read-only/Inferred. Source craftsmanship level and XP share the address; no false independent rank is shown. |
| Regional markers | Existing `progress.source.md` bit IDs; six bundled regional backgrounds, no candidate coordinate arrays | Source candidates/Inferred, Found/Missing is only bit 1/0. Separate local fields share the original write warning. |
| Restock preset | Existing positive ordinary `Items` count words; target 1–99, never reduces | No zero/unknown/key/currency/gear/card allocation. 99 is editor policy. F6 preview/group undo. |
| Star preset | Existing ordinary AP u32 / stored-star u8 pair | AP becomes the chosen strict-curve tier threshold. Summons/mismatches excluded. F6 distinguishes primary stars from dependent AP. |

## Remaining owner capture checklist (#31)

Retain the detailed seven-step README checklist for each Remake main/INTERmission/retail/demo variant.
Add these exact events, each as a separate same-slot decrypted before/after pair with menus and
title/patch/chapter/difficulty/slot/character/region recorded:

1. Purchase then undo/respec one Remake weapon-upgrade node: node name/grid location, SP before/after,
   new socket count/link, stats/ability effects, equipped weapon/type and every materia instance.
2. Purchase one Rebirth folio node on each of the seven characters; include SP, prerequisites,
   unlocked ability/synergy and pre/post raw byte. Separately reset that folio.
3. Craft one item and one armor upgrade separately; record recipe unlock/done, ingredient counts,
   resulting owned instance/detail, crafting XP/rank and rewards.
4. Win/claim one minigame rank reward separately; include personal best, rank, reward flag,
   inventory and any actor/quest change. Include enemy assessment/defeat and relationship event pairs.
5. Validate one restock and one ordinary-star preset on a disposable slot per variant: load, verify
   counts/abilities/socket owner, battle/use an item, resave/decrypt/reimport, then verify persistence.

## Scope exclusions

No runtime battle cheats, guessed 100%-all completion recipe, fabricated weapon-upgrade DAG,
unverified candidate coordinates, unsafe structural allocation, common-file synchronization or
unsupported-platform conversion. These would assert semantics the inspected sources/samples do not prove.
No additional agents, pushes or merges were used. F12/F13 adoption is recorded in README;
toolkit-owned persistent loadout storage remains a separately owned SDK request.
