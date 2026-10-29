# Pragmata — improvement plan

> **Historical planning snapshot — 2026-10-28.** The baseline, proposed work, test counts and
> policy restrictions below describe that research round, not current implementation or permissions.
> See [current plugin status](../PLUGINS.md), the linked plugin README,
> [delivered framework features](ROADMAP.md) and [current policy](../PLUGIN_GUIDE.md#hard-rules).

## Summary
Pragmata is an Experimental, fixed-size editor for PPSA02530: it decrypts DSSS/Mandarin, parses the hash-addressed RE Engine stream, edits primitive fields and existing inventory quantities, repairs CityHash/Murmur3, and exposes a bounded raw tree. “Massively better” means a named, confidence-labelled editor for inventory/resources, loadouts, character progression, objectives and collectibles, with safe bulk actions, presets, icons and useful diffs. The three blockers are (1) no canonical item/quest/field dictionary tied to the IDs in these PS5 saves, (2) no evidence or serializer for structural changes and legal ranges, and (3) no controlled before/after samples or console load test. The plan therefore reads and maps first, adds writes only after differential evidence, and keeps unknown data lossless.

## Baseline state (historical evidence)
- plugins/Pragmata/SaveToolkit.Plugins.Pragmata/Crypto/DsssContainer.cs and Crypto/Mandarin*.cs implement DSSS v2, flags 0x10, PS5 key 526253637, AES-128-OFB, embedded key/IV validation, per-block CityHash64 and outer Murmur3 repair. Rebuild is size-preserving and retains unknown metadata, padding and the RSA trailer.
- Format/TypedReader.cs, Format/SaveNode.cs and Format/SaveLayout.cs parse the typed, hash-addressed stream with the verified origin-16 alignment. Layout locates the gameplay header, app.InventoryManager.SaveData (3a83076c), _InventoryItems (3185b7a5) and app.GameClock.GameSaveData (7d74c37a) by hashes rather than array position.
- PragmataDocument.cs exposes every fixed-size primitive it has shown, applies batches atomically, reports aliases/derived clock values for undo, and re-opens serialized output. It cannot resize arrays or edit strings/opaque structs.
- Views/InventoryView.cs shows 90 existing item rows as ID/quantity/equipped text and offers “set every existing quantity”; IDs have no names, descriptions, categories or legal caps.
- Views/PragmataViews.cs provides Overview/Inventory/Clock/Raw tabs. Views/RawExplorer.cs and Views/LazyPages.cs bound large trees to lazy pages; the SDK has no record reference, picker, map, diff, or add/remove-row node.
- Format/HashNames.cs embeds 413 hash-checked identifiers (all 288 sampled field names and 125/127 sampled class names), but identifier matches do not prove gameplay semantics or legal values.
- SaveToolkit.Plugins.Pragmata.Tests/SampleEvidenceTests.cs, EditingTests.cs, ClockTests.cs and SessionIntegrationTests.cs cover all 12 distinct .bin files, no-op identity, quantity edits/reversal, clock invariants, corruption rejection and framework undo. The README records 503 Mandarin blocks and 519,872 class records; no console load has been performed.

## Research findings
### Existing editors/tools (same game, other platforms)
- [REE Save Editor](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/README.md) (commit 4125e497f1ae84cc746bb4a99da612c48bea161e, 2026-06-19) lists Pragmata PC (Steam AppID 3357650) and PS5, both Mandarin, and says PS5 uses ID 526253637 plus _RegionHash=3208943443. This independently supports the container/key lead; our samples verify the DSSS header, region hash and Mandarin output claims, but no PC save or console transfer was tested. The repository has no declared SPDX licence, so consult algorithms/data only; do not port code or ship its dumps without permission.
- [MandarinJuice](https://github.com/mi5hmash/MandarinJuice/tree/11a7e99c2189a99b6ee0a874d2c4ffc6221a5942) (commit 11a7e99c2189a99b6ee0a874d2c4ffc6221a5942, 2026-09-04, MIT) decrypts/encrypts/re-signs Steam RE Engine saves using game profiles. It is useful for PC account/profile workflows, not PS5 outer-container encryption or item semantics; transfer-to-PS5 confidence is low.
- Cheat Engine/Open Cheat Tables’ [Pragmata thread](https://opencheattables.com/viewtopic.php?f=15&p=4916&t=1870) (v7 post dated 2026-04-26) exposes runtime currency labels, app.InventoryManager, app.EnhanceManager and hacking/training controls through REFramework. It is a Windows runtime lead, not a persistent-save map; table licensing/redistribution is unclear and nothing was verified against the PS5 bytes.
- [Save Wizard’s FAQ](https://www.savewizard.net/faqs/) says PS5 games are unsupported (only PS4 games running on PS5); [Apollo Save Tool](https://apollosavetool.com/ps4/) is PS4/GPL-3.0. No PS5-native Pragmata editor was found. [Capcom’s platform FAQ](https://www.capcom-support.com/hc/en-us/articles/26854337316124-PRAGMATA-Frequently-Asked-Questions-All-Platforms) explicitly says cross-saves are not supported, so PC/PS5 structural equivalence must be proven rather than assumed.

### Same engine / same studio
- [REasy](https://github.com/seifhassine/REasy/tree/2c9bc63ec3b4d3bd0d423408ffa97ad12db0b80e) (commit 2c9bc63ec3b4d3bd0d423408ffa97ad12db0b80e, 2026-10-27, MIT; PySide6/LGPL notices apply) supports Pragmata and RE Engine siblings (RE2/3/4/7/8/9, DMC5, SF6, Monster Hunter Rise/Wilds, Dragon’s Dogma 2) and edits RSZ/MSG/PAK/assets. It is valuable for type/asset extraction and differential tooling; it does not establish Pragmata save fields or PS5 legality.
- [REFramework](https://github.com/praydog/REFramework/tree/d4611aef1d63effd3a736bdf9404ea702428017d) (commit d4611aef1d63effd3a736bdf9404ea702428017d, 2026-10-21, MIT) provides RE Engine IL2CPP type discovery, Lua hooks and object exploration on PC. Use it to capture a field’s runtime value while making one in-game change, then correlate to a save delta; runtime-to-PS5 transfer remains unverified.
- [RE_RSZ](https://github.com/alphazolam/RE_RSZ/tree/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e) (commit 871a1d5c4c7b81c60c966d73ee63f4c4413ab56e, 2026-03-30, MIT) and REFramework RSZ dumps demonstrate reusable class/field schemas for sibling games. The tool targets RSZ assets rather than the Mandarin save stream; it can supply names/types but not save offsets, IDs or limits.
- The RE Engine family is evidence for shared serialization concepts, not a licence to copy game schemas. Each sibling has different class maps, versioning, keys and integrity coverage; every Pragmata claim still needs a PPSA02530 sample check.

### Game data sources (names, descriptions, icons, limits, quests)
- The pinned REE assets [rszpragmata.json](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/rszpragmata.json), [enumspragmata.json](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/enumspragmata.json) and [strings_pragmata.txt](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/strings_pragmata.txt) are JSON/UTF-8 dumps. They resolve class/field names such as app.ItemInventoryInfo, _Quantity, app.ObjectiveManager.SaveData, app.GameOccurrenceEventManager.SaveData, app.CheckPointManager.SaveData, app.EquipmentPerk.SaveData and app.WeaponManager.SaveData; they do not provide a proven mapping from item IDs in the 90 sampled rows to names, descriptions, icons or stack caps. The upstream repository has no declared licence, so keep these as owner-supplied/reference inputs until redistribution is cleared.
- [PowerPyx’s collectible guide](https://www.powerpyx.com/pragmata-collectibles-guide-100-completion/) supplies named categories and counts (15 Mini Cabins, 27 Escape Hatches, 70 Safe Boxes, 21 Pure Lunum, 16 REM, 22 Mods, 5 Storage Expanders, 2 Cartridge Holders, 6 Training Data) and five sectors. [VGTIMES’ interactive map](https://vgtimes.com/guides/155080-pragmata-interactive-map-all-collectibles-markers-icons-locations.html) adds location/category names and marker coverage. These are HTML editorial/map sources, not ID tables; use for labels and human cross-checks only, with source attribution and no automatic claim that a hash/ID matches.
- [Pragmata Fandom’s item category](https://pragmata.fandom.com/wiki/Category%3AItems) and [Fandom licensing](https://www.fandom.com/licensing) make article text generally CC BY-SA with attribution/share-alike. Fandom states that non-text media may have separate or fair-use rights ([media guidance](https://support.fandom.com/hc/en-us/articles/360035075654-I-want-to-reuse-text-or-images-from-a-Fandom-wiki)); do not embed its icons. Descriptions can be imported only with attribution and a compatible licence.
- PC Gamer’s [progression guide](https://www.pcgamer.com/games/third-person-shooter/pragmata-guide/) confirms player-facing currencies (Upgrade Components, Lunafilament, Pure Lunum, Cabin Coins), vendors and uses, but gives no save IDs or legal maxima. Treat it as a semantic hypothesis until a controlled save delta and runtime observation agree.
- Practical data route: ship only a small, hash-verified manifest of names/types that the owner is allowed to redistribute; accept an optional user-supplied PC extraction for strings/icons; or fetch no network data in the plugin and let the UI import a separately licensed manifest. Capcom’s policy warns against distributing official icons as standalone assets ([video policy](https://www.capcomusa.com/video-policy/)), so icons should be user-supplied, generated from a licensed source, or omitted.

### Integrity / crypto
- The local docs/research/pragmata/FORMAT.txt, verified_save_map.json and independent C# implementation establish DSSS v2/Mandarin 0x10, variable capacities, mixed-state PRNG, AES-OFB, ElGamal-style key/IV metadata, CityHash64 block hashes and Murmur3 outer hash. [REE Mandarin source](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/src/save/crypto/mandarin.rs) and [game/type sources](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/src/save/game.rs) are algorithm references, not copied code. CityHash’s [MIT implementation](https://github.com/google/cityhash/blob/f5dc54147fcce12cefd16548c8e760d68ac04226/src/city.cc) is recorded in THIRD_PARTY_NOTICES.txt; AES-OFB vectors are from [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final). The missing pieces are RSA trailer generation, future flags/compression, array/string reserialization and the PS5 outer save encryption/signing layer.
- Independent sample check on 2026-10-28: read the private archive without extraction; found 12 data*.bin entries, all DSSS v2/flags 0x10, and matched every SHA-256 and declared plaintext length in verified_save_map.json (503 blocks, 519,872 class records, 90 inventory rows in each of 11 gameplay files). No repository tests or downloaded code were executed in this planning pass; the map’s recorded no-op/edit/corruption results and the plugin tests remain the evidence for full parse/integrity behavior. No console load is verified.

## Vision
- Dashboard: slot/date/playtime, cleared state, difficulty and current chapter with verified/inferred/unknown badges, integrity summary, source/version/region hashes, and before/after byte/value diff.
- Inventory & resources: searchable named rows with icon, category, description, quantity, legal range, equipped state and provenance; gadgets/shortcuts and currencies separated from ordinary items; “set legal max”, “set all selected”, and existing-row-only bulk actions with undo.
- Loadout & character: weapons, passive perks, active skills, hacking style, presets, capacities and character/resource fields once each field is mapped and tested.
- Progress: chapter/checkpoint rail, objectives, occurrence/event completion, gimmicks, training simulations, REM/mod/figure/escape-hatch/Red-Zone counters and sector completion. Unknown flags stay raw and labelled.
- Raw/integrity: lazy typed tree, plaintext/ciphertext offsets, hashes, unknown bytes, and a safe diff; no structural write is enabled until the serializer proves preservation.
- No “grant arbitrary item”, guessed currency label, or icon is exposed as authoritative. Structural add/remove/reorder is a later, explicitly experimental capability.

## Phased plan
### Phase 1 — Evidence and semantic inventory (M)
Goal: turn names/types into save-backed, confidence-labelled fields.
- [ ] Re-index all 27 gameplay section classes against the pinned RSZ dump; record hash, field path, type, array count and sample variance.
- [ ] Build a research-only ID manifest for inventory, gadgets, weapons, perks, objectives, event records, checkpoints and chapter hashes; keep Verified, Inferred and Unknown separate.
- [ ] Use REFramework/CE on PC to capture one runtime operation and pair it with before/after PC and PS5 saves; never infer from a single slot.
Evidence: at least three independent deltas per family and an owner-approved source/licence manifest. Risk: PC and PS5 schemas may diverge. Size: M.

### Phase 2 — Reference data and friendly read-only views (M)
Goal: make the current fixed-size editor useful without unsafe writes.
- [ ] Embed only redistributable hash/type names and a versioned manifest; support user-supplied strings/icons as an external reference-data package.
- [ ] Add named Inventory, Gadgets, Loadout, Progress and World sections; preserve raw IDs beside friendly labels and show provenance.
- [ ] Add read-only item descriptions, categories, maps and counters from licensed/reference data; mark missing mappings explicitly.
Evidence: every displayed name maps to a hash/ID and a cited source. Risk: copyrighted assets and ambiguous IDs. Size: M.

### Phase 3 — Validated primitive editing and bulk actions (M)
Goal: expand safe edits before structural changes.
- [ ] Add enums/flags and narrow ranges only where differential samples establish them; retain raw custom values with warnings.
- [ ] Add existing-row edits for gadgets, perks, weapons, checkpoints and progress flags; add selection-based bulk actions and presets with complete undo changes.
- [ ] Add preflight validation (cross-field dependencies, derived totals, duplicate IDs, overflow) and an explicit “unknown side effects” warning.
Evidence: edit/reopen/reverse fixtures for each field family plus human-confirmed in-game effect. Risk: flags can unlock inconsistent state. Size: M.

### Phase 4 — Structural save support (L)
Goal: support add/remove/reorder only when lossless serialization is proven.
- [ ] Implement a preserve-first typed writer for strings, structs, class arrays and metadata, with byte-identical no-op output across versioned samples.
- [ ] Add transaction support for dependent arrays and multi-file system/gameplay edits; refuse edits when unknown metadata or version cannot be preserved.
- [ ] Fuzz malformed counts/alignment/duplicate hashes and compare parser/writer output against independent Python/reference tools.
Evidence: controlled add/remove samples, round-trip byte diffs limited to intended records/checksums, and console load. Risk: highest corruption/data-loss risk. Size: L.

### Phase 5 — Framework/UI integration (L)
Goal: deliver the real editor experience.
- [ ] SDK/Core: add typed record identity, references between fields, optional row add/remove operations, reference-data manifests/assets, diff/preflight results and multi-file atomic export.
- [ ] UI: item picker with search/icon/description/range, grouped grids, map/quest views, preset/diff panels, selection-aware bulk actions and per-field provenance.
- [ ] Keep lazy paging and ensure actions report undoability when rows are not exposed.
Evidence: API/UI contract tests, accessibility checks, and performance tests on worst-case trees. Risk: shared SDK changes affect every plugin. Size: L.

### Phase 6 — Console validation and maturity (S)
Goal: earn Stable status for a bounded feature set.
- [ ] Restore an unchanged dump, then one legal quantity/resource change, then one progression change on a spare slot.
- [ ] Test backup/restore, export/re-import, region/title updates and failure recovery; document exact firmware/game version.
- [ ] Promote only tested fields to Stable; leave all others Experimental/ReadOnly. Evidence: owner’s console logs and re-opened saves. Size: S.

## Requirements
| ID | Area | Requirement | Why / which phase needs it | Blocking? | Workaround today |
|---|---|---|---|---|---|
| PRAGMATA-R1 | Data | Versioned ID manifest mapping stored IDs/hashes to names, descriptions, categories, sources and confidence | Phase 1–2; prevents invented labels | Yes | Show hex/decimal IDs and cited hash names |
| PRAGMATA-R2 | Data | Legal range/stack-cap manifest with per-version evidence and cross-field rules | Phase 3; int32 is storage range, not game legality | Yes | Keep full storage range plus warning |
| PRAGMATA-R3 | Data | Licensed icon/asset route (embedded, user-supplied, or generated) with attribution metadata | Phase 2/5; Capcom icons cannot be bundled by default | Yes for icons | Omit icons and show text |
| PRAGMATA-R4 | Samples | Before/after saves for one pickup/use/purchase/equip/upgrade/quest/collectible change, including system and gameplay files | Phases 1–4; identifies fields and dependencies | Yes | Read existing 12 saves only |
| PRAGMATA-R5 | Samples | Saves across difficulty, NG+/cleared state, chapters, empty/full inventory and all item families | Phases 1–3; establishes ranges and optional records | Yes | Label unknown/inferred |
| PRAGMATA-R6 | Human | Owner supplies PC/runtime access and approves capturing user-owned game data | Phase 1; REFramework is PC-only | Yes | Rely on public guides, no authoritative mapping |
| PRAGMATA-R7 | Human | Console owner performs unchanged, quantity and progression load tests on a spare slot | Phase 6; no code test proves console acceptance | Yes for Stable | Keep Experimental |
| PRAGMATA-R8 | Tooling | Differential-save harness that decrypts, canonicalizes paths, reports changed bytes and repairs hashes independently | Phases 1–4; guards against false correlations | Yes | Existing fixed-size tests and map |
| PRAGMATA-R9 | Tooling | Versioned fixtures/fuzz corpus for alignment, truncation, duplicate hashes, arrays, strings and unknown metadata | Phase 4; structural writer must never lose data | Yes for structural edits | Reject unsupported structures |
| PRAGMATA-R10 | Core | Reference-data package loading/version checks and provenance surfaced in sessions/export | Phase 2/5; plugin cannot do I/O/network | Yes for external dictionaries | Embed current 413-name resource only |
| PRAGMATA-R11 | Core | Multi-file atomic transaction, export preflight and value/byte diff | Phase 4–5; progression edits may span files | Yes for multi-file edits | Rewrite one claimed .bin only |
| PRAGMATA-R12 | SDK | Record identity and references, typed arrays/rows, optional add/remove/reorder and derived-change reporting | Phase 4–5; current FieldNode/TableNode cannot express structure | Yes for structural UI | Existing value edits/actions |
| PRAGMATA-R13 | UI | Item picker/grid with icons, descriptions, ranges, provenance and selection bulk actions | Phase 2–5; current table has ID/quantity only | No for raw editor | Searchable ID table |
| PRAGMATA-R14 | UI | Progress/map/quest/diff panels with lazy loading and unknown-state display | Phase 2/5; current vocabulary has no map/diff node | No for first release | Sections, tables and TextNode warnings |
| PRAGMATA-R15 | Human | Decisions on whether user-supplied copyrighted data may be imported and which labels are acceptable | Phase 2; controls licensing and product wording | Yes for friendly data | Use raw/hash labels |
| PRAGMATA-R16 | Integrity | Evidence or implementation for RSA trailer generation, future DSSS flags/compression and PS5 outer re-encryption | Phase 4/6; needed for new layouts/exports | Yes when encountered | Reject unsupported versions and preserve trailer |

## Acceptance criteria
- [ ] Every friendly label has a stable hash/ID, source URL or manifest record, confidence and version; unknown values remain visible as raw values.
- [ ] The 12 supplied files still parse completely and no-op serialize byte-identically after each phase.
- [ ] Controlled delta fixtures identify and reopen each newly editable item/resource/loadout/progress family; only intended plaintext bytes and required checksums change.
- [ ] All editable fields have evidence-backed ranges, cross-field validation and atomic rejection tests; bulk actions are undoable or explicitly marked irreversible.
- [ ] Strings, structs, arrays, padding, unknown metadata, unused capacity and trailers survive round trips; unsupported versions fail closed.
- [ ] Reference data and icons obey their licence route; no unlicensed Capcom/Fandom media is embedded.
- [ ] UI supports search, lazy paging, provenance, warnings, selection bulk actions, diff and export preflight without exposing unbounded trees.
- [ ] A human restores an unchanged dump and loads one quantity/resource edit and one progression edit on a spare PS5 slot; the tested subset is the only subset promoted to Stable.
- [ ] API/SDK/Core changes have contract and regression tests, and the plugin remains stateless and dependent only on the SDK.

## Open questions & risks
- Which of the 90 ItemInventoryInfo IDs are currencies, ammo, consumables, weapons, mods or quest items, and are IDs stable across PC/PS5/title updates?
- Do ObjectiveManager, GameOccurrence*, CheckPointManager, GimmickManager, EquipmentPerk, WeaponManager and GuiManager arrays encode completion, unlock, display and runtime caches independently?
- Are item caps per item, difficulty, chapter or mode? Does setting a stored-valid value trigger overflow, missing unlocks or progression soft-locks?
- Does the console validate a signature or RSA trailer beyond the verified inner hashes, and does the PS5 container require sibling-file metadata changes?
- Can user-owned PC extraction legally supply strings/icons, and should the product support an import-only reference package rather than redistribute assets?
- How do title updates alter RSZ hashes, enum values, Mandarin flags, capacities or field ordering?
- Public guides disagree on some collectible totals; all counts and chapter mappings must remain Inferred until a matching save delta or in-game check confirms them.


