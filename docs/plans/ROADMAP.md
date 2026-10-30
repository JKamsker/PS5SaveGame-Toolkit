# PS5 Save Toolkit: cross-plugin requirements roadmap

For the current disposition of the remaining 17-title work in issue #81 section B, see
[the title-gap triage](TITLE_GAP_TRIAGE.md): bounded READY tasks, exact #31 capture requests,
unresolved integrity evidence and work not justified by the current implementation.

## Current status — 2026-10-29 (#40)

This status supersedes the historical proposals below. F1–F14 are implemented on local `main`;
feature availability does not imply every plugin uses every feature, or that an edited save has
been accepted on PS5. All plugins remain Experimental. See the [current support table](../../README.md#supported-games-and-formats),
[plugin guide](../PLUGIN_GUIDE.md) and [owner checklist](../OWNER_CHECKLIST.md).

| Feature | Delivered capability | Tracking |
| --- | --- | --- |
| F1 | Read-only byte/semantic SaveDiff and fixture manifests | [#2](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/2) |
| F2 | Pinned embedded/user reference packs, provenance and local assets | [#3](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/3) |
| F3 | Rich live table cells, details, filtering and selection | [#4](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/4) |
| F4 | ReferenceSpec, searchable field/action picker and eligibility checks | [#5](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/5) |
| F5 | Stable identities, structural mementos and mixed undo/redo | [#6](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/6) |
| F6 | Revision-bound action preview/apply and original/current semantic review | [#7](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/7) |
| F7 | Explicit multi-file document groups with staged transactions/history/export | [#29](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/29) |
| F8 | Bounded server-side collection queries and stable row keys | [#10](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/10) |
| F6 + F8 | Whole-query action selection, unloaded rows and exclusions | [#27](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/27) |
| F9 | Bounded maps/DAGs, text alternatives and attributed local data/external links | [#30](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/30) |
| F10 | Exact numeric lexemes, explicit null and tagged union values | [#9](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/9) |
| F11 | Managed Kraken decoder/stored writer and in-memory SQLite operations | [#28](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/28) |
| F12 | Bounded F8 SaveDiff traversal, SDK decoder hooks and CLI discovery/selection | [#32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32) — implementation delivered |
| F13 | Isolated non-mutating inspection, browse-only picker, catalogue filter/sort and float durations | [#33](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/33) |
| F14 | One inventory table with confirmed empty-slot visibility and full-model counts | [plugin guide](../PLUGIN_GUIDE.md#inventory-tables-and-empty-slots-f14) |

Shared collection/action/binary/reference/CRC helpers, storage-only field registration and conformance
mutation fuzzing are also delivered. The [field-metadata proposal](F13-FIELD-SPEC-PUBLISHING.md) is
**partially delivered** overall: [SDK-02](../SDK_REQUESTS.md#prioritized-inventory) delivers opt-in
canonical field metadata, primary/derived binding policy, unloaded-edit preflight and live replay
integration, and Pragmata has adopted it. Targeted original/current semantic-review metadata capture,
generic cross-ID alias relationships and automatic group forwarding remain separate integrations.
Three SDK requests remain open: lossless JSON consumer adoption (SDK-06, large), versioned preset
storage (SDK-07, large), and SFO subtitle ownership/synchronization (SDK-10; blocked on evidence).
SDK-08 is resolved for the production UnrealGvas consumer now on `main`; its eligible non-generic
Jedi/Sackboy projections use canonical references with bounded fallbacks and consumer tests. The
remaining gates are recorded in the [current backlog](../SDK_REQUESTS.md#prioritized-inventory).
The open rows retain their foundation/adoption boundaries; no foundation alone closes them.

The backend quality pass [#34](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/34) and web quality
pass [#35](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/35) have also landed. Their reported
fixes/tests are delivered; this does not promise further unperformed review. GitHub issue state is
separate from implementation status.

The delivered baseline above was rechecked against `201dd690a48a93bf496f1825246bc6954cc5ec52`,
which includes merged PR56 ownership, PR59 C# size guard, PR66 UnrealGvas adoption and PR70 idle-race
test fix. Three open SDK requests retain a clearer foundation/adoption split:
| Request | Foundation on `main` | Adoption/evidence gate still open |
| --- | --- | --- |
| SDK-06 lossless JSON | `LosslessJsonDocument`, bounded limits, scalar replacement and snapshots, with Core characterization tests | Both GenericJson and XorJson consumers must land on `main`; their structural IDs and XOR envelopes remain plugin-owned. |
| SDK-07 presets | No host preset store yet; existing F6 plans remain usable | Host storage plus title/build and owned-instance resolution, then the FinalFantasy7 and transfer use cases. |
| SDK-08 canonical references | `FieldReferenceNode`, `FieldPresentation`, metadata resolution and bounded normalization, plus production UnrealGvas projections and consumer tests | Resolved for eligible non-generic Jedi/Sackboy projections; other titles still need their own evidence and gameplay semantics remain unproven. |
| SDK-10 SFO caption ownership | `ParamSfoDocument`, identity-bound pure plans, strict UTF-8/capacity/NUL checks and tests | Reviewed ownership plus controlled whole-byte caption evidence and a later SuckerPunch consumer. |

The functionality/UX improvement wave remains unmerged and awaits integration validation and review.
This table does not close any ledger row, change the historical F1–F14 baseline, or close gameplay,
format and owner-evidence gaps below.

### Plugin rounds

The reader/reference/view rounds and write-enable round have landed for all 17 plugins. Completion
work below records the shipped implementation subsets and documented gaps,
**not every phase exit of the original plan**. The per-plugin plans are superseded historical snapshots.
Console effects, controlled samples, matching data and unsupported dependency writers remain explicit gaps.
F12/F13 adoption varies by README; a plugin's old starting-main note does not make the framework pending.

| Plugin | Completion-round status / remaining scope |
| --- | --- |
| [AstroPlayroom](../../plugins/AstroPlayroom/README.md) | Complete; collectible/world/coin presets, F8/F9 and inspection; bot/structural grammar still unknown |
| [AtomicHeart](../../plugins/AtomicHeart/README.md) | Complete; populated inventory edits, query batches, evidence graphs/map and decoder/inspection; inner integrity/codec acceptance still unproven |
| [BaldursGate3](../../plugins/BaldursGate3/README.md) | Shipped bounded NewAge writes/views ([#1](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/1)); authoritative identities and broad structural/story/dependency writers remain implementation limits |
| [Cyberpunk2077](../../plugins/Cyberpunk2077/README.md) | Complete; stored progression/facts/stacks, broader read-only views and F12/F13; equipment/perk/world transaction writers incomplete |
| [EldenRing](../../plugins/EldenRing/README.md) | Complete; six existing operations, progress/class references, F8/F9 and F12/F13; broader stats/equipment/world dependencies unmodeled |
| [FinalFantasy16](../../plugins/FinalFantasy16/README.md) | Complete; fixed-width edits, inventory query actions, ability/location references and F12/F13; allocator and saved ability joins need evidence |
| [FinalFantasy7](../../plugins/FinalFantasy7/README.md) | Complete; supported variant writes, restock/star presets and F12/F13; full socket/upgrade/reward/structural model remains incomplete |
| [Gearbox](../../plugins/Gearbox/README.md) | Complete; serial-part editing, structural inventory/F7, skill references and F12/F13; legal pools/account precedence and BL4 evidence remain open |
| [GenericJson](../../plugins/GenericJson/README.md) | Complete; title adapters, bounded browsing, F9, decoder/inspection; missing title definitions/dependency templates need captures |
| [GodOfWarRagnarok](../../plugins/GodOfWarRagnarok/README.md) | Complete; counters/time/restore and F9/F12/F13; integrity and gear/story grammar remain unknown |
| [Insomniac](../../plugins/Insomniac/README.md) | Complete; scalar writes/bolt planning, F8/F9 and F12/F13; inventory identity, upgrade and location joins need captures |
| [Pragmata](../../plugins/Pragmata/README.md) | Complete; six added write surfaces, retained quantities/clocks, mission references and F12/F13; RSA and structural resizing remain unsupported |
| [SuckerPunch](../../plugins/SuckerPunch/README.md) | Complete; Tsushima quantities/timer, reference F8/F9 and F12/F13; Yōtei has 14 eligible existing quantity types and reviewed targets; checksum-tail coverage and console acceptance remain unknown |
| [TalesOfArise](../../plugins/TalesOfArise/README.md) | Complete; existing scalars/consumables and query planning, reference graphs and F12/F13; equipment/CP/growth/progression writers absent |
| [UnrealGvas](../../plugins/UnrealGvas/README.md) | Complete; warned Jedi/Sackboy scalars, Hogwarts cells and F12/F13; checksums, row-role dependencies and stored-frame acceptance remain unproven |
| [Witcher3](../../plugins/Witcher3/README.md) | Complete; progression/items/skills/Gwent and repair/refill, F9/F12/F13; structural identity and related-system transactions absent |
| [XorJson](../../plugins/XorJson/README.md) | Complete; supported offline scalar/structural/F6/F7, F8/F9/F12/F13; gameplay semantics and console effects remain Unknown |

Owner work is consolidated in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
Implemented writes are enabled with warnings; capture requests do not hide a prepared writer behind
another permission decision. Licence exclusions and missing asset/rights evidence are recorded in
[the central register](../LICENCE_EXCLUSIONS.md).

## Original plan (historical; retained below)

The following text is the original planning snapshot. Statements such as “Today”, “Proposal” and
“Decisions needed” describe that snapshot, not current missing framework features or permissions.

Synthesised from 17 per-plugin plans (`docs/plans/<Plugin>.md`) and 353 requirements
(extracted from the plans), 2026-10-28. Each plugin plan uses issue #1 (Baldur's Gate 3) as the
quality bar. Counts below are the number of plugins whose plan raised the need, out of 17.

## Summary

The plans agree on one thing above everything else: **the biggest blockers are evidence and
rights, not code.** Every plugin is blocked on the same three non-code items. Behind those, six
framework capabilities come up in almost every plan. Built once in SDK/Core/UI, they serve all
plugins.

| # | What | Plugins | Kind |
|---|---|---|---|
| B1 | Controlled before/after PS5 save pairs | 17 | Samples (owner) |
| B2 | Owner's on-console validation (load, effect, re-save, reload) | 17 | Human (owner) |
| B3 | Licensing policy for names, descriptions and icons (embed, user-supplied or none) | 17 | Human decision |
| F1 | Shared differential-save tooling | 17 | Tooling |
| F2 | Reference-data and asset packs (SDK context at `Open` + Core import/cache) | 17 | SDK + Core |
| F3 | Rich `TableView`: image/detail cells, confidence, live filtering, selection | 14 | UI (quick win) |
| F4 | Catalog-reference spec + searchable item picker | 14–15 | SDK + UI |
| F5 | Stable identities + reversible structural edits (add/remove/reorder with undo) | 17 | SDK + Core |
| F6 | Action preview/dry-run + semantic before/after review | 15–16 | Core + UI |

Less common, but blocking for specific plugins:
- **F7** multi-file atomic transactions (13, mostly conditional);
- **F8** paged/queryable collections (12, needed at catalog scale);
- **F9** map/graph nodes (about 14, almost all optional);
- **F10** exact JSON numbers and nullable values (GenericJson, XorJson);
- **F11** codec routes: Oodle for AtomicHeart, Kraken and SQLite for UnrealGvas/Hogwarts Legacy.

## 1. The three universal blockers: owner actions

These can't be delegated to agents. Without them, plugins stay `Experimental`/`ReadOnly`
whatever the code quality.

**B1: Controlled sample pairs (17/17).** Every plan asks for saves taken right before and after
one known in-game change: buy one item, gain one level, equip one piece, collect one collectible.
Single snapshots can't prove which bytes encode quantity, ownership or limits.
- *Proposal:* a short **capture protocol** in `docs/SAMPLES.md`, expanded into the consolidated
  [owner capture and console-validation guide](../OWNER_CHECKLIST.md). Per title it lists the 5–10
  smallest events that pin down the main fields, how to name the files
  (`<title>_<event>_before/after`), and what to note (game version, slot, DLC).
- The per-plugin plans already list exactly which events they need.

**B2: Console validation (17/17).** "Loads without corruption" isn't enough: does the edit take
effect, does the game re-save it, does it survive a reload?
- *Proposal:* a per-feature **validation checklist** template stored next to each plugin.
- Only a human may set `Stable`, which already matches the plugin rules.

**B3: Reference-data rights (17/17).** Names, descriptions and icons come from wikis (often
CC BY-SA), datamines and game files. Many plans mark icons as not redistributable.
- Decisions needed:
  - May CC BY-SA text be embedded, with attribution?
  - Are copyrighted icons excluded from the repo and only loaded from user-supplied packs?
  - What spoiler default should apply?
- This one decision unblocks F2 for every plugin.

## 2. Framework roadmap

### F1: Differential-save tooling (17/17, blocking for evidence)
Today every plugin wants its own private diff harness. Build one shared, read-only tool instead:
- open two save sets;
- keep ZIP entries that occur more than once, and their generations;
- align records by parsed identity, not by offset;
- ignore known clock and relocation churn;
- report changed byte ranges and semantic paths.

Plugins provide a decoder hook. Typical cases:
- Unreal property trees;
- Larian LSMF (Baldur's Gate 3);
- Insomniac DDL;
- Atomic Heart records.

*Where:* `src/SaveToolkit.Testing` or a small `tools/` CLI. It is a dev tool, not a plugin, so
the plugin rules still hold.
*Pairs with:* the B1 capture protocol, plus a fixture manifest format (build/platform/event/
expected values) shared by all test suites. 16/17 plans asked for a fixture or corpus manifest.

### F2: Reference-data and asset packs (17/17 Core; 11 explicitly asked for an SDK hook)
**Today:**
- `IGamePlugin.Open(ISaveFile)` has no channel for reference data.
- Plugins may only use embedded resources, which rules out user-supplied names and icons.

**Proposal:**
- **SDK:** add an optional, read-only `IReferenceContext` passed at open. Keep the
  parameterless `Open(ISaveFile)` working for backward compatibility. It exposes:
  - pack ID, version, game build and locale;
  - limited-size lookups;
  - asset IDs.

  Plugins stay stateless and do no I/O.
- **Core:** add a pack importer and cache:
  - manifest with schema, title, build, locale, licence/attribution and per-file hashes;
  - size and path guards, and safe image conversion;
  - storage outside save directories;
  - the pack used is recorded in session metadata and export reports.
- **Data:** define one shared catalog format (JSON/CSV with ID, name, category, limit,
  description, asset reference and source), so each plugin's data work is only content.
- *Depends on:* B3. Embedded attributed text can ship before the import UI exists.

### F3: Rich `TableView` (14/17, quick win)
Checked in `web/src/components/document/TableView.tsx`:
- It renders only `text` and `field` cells; image, action and other nodes come out **blank**.
- The row filter matches the value the view was loaded with, not the live edited value
  (`cellText` reads `node.value`, not the value from `useLiveField`).

Wanted:
- image/thumbnail cells with alt text;
- an expandable detail row;
- visible **confidence badges**, descriptions and validation errors;
- category filters;
- explicit row selection (needed for F6 bulk actions);
- fixed-height virtualisation kept intact.

This is pure UI work with no SDK change, and has the highest value per effort.

### F4: Catalog-reference spec + item picker (14 SDK / 15 UI)
- **SDK:** a reference-valued `FieldSpec`. It has a catalog key and stable raw ID, plus label,
  description, category and asset ID, and availability/eligibility reasons. It keeps unknown
  current values (shown as raw ID) and supports search/paged candidates.
- **UI:** an accessible, searchable picker with icons, category/owned filters and a raw-ID
  fallback.
- *Depends on:* F2 for large catalogs. Small embedded enums work today.

### F5: Stable identity + reversible structural edits (17/17; blocking for add/remove)
**Today:**
- `ActionOutcome.Undoable=false` for structural changes.
- `StructureChanged` has no inverse, so adding or removing records clears history.

**Proposal:**
- stable logical row/record IDs that survive view rebuilds;
- typed references between fields and records;
- a reversible structural operation: an opaque document memento or an inverse-op token, with
  atomic preconditions;
- Core history that replays it, rebuilds `ViewIndex`, retires stale value/action/lazy IDs, and
  rejects stale client calls.

This is the largest SDK change. Design it once (GenericJson and Gearbox are good first
consumers), then roll it out.

### F6: Action preview + semantic review (16 / 15)
- A pure **plan/preview** step for bulk actions and presets (respec, loadouts, "give all"). It
  returns:
  - named old/new values;
  - derived changes;
  - skipped rows and warnings;
  - a document-revision token.
- Then an atomic **apply** that revalidates the plan, recorded as one undo unit.
- **UI:**
  - a preview dialog with explicit scope and count;
  - a semantic before/after diff against the original save, with jump-to-field;
  - a "changed only" filter.

### F7–F11: Narrower needs
- **F7 Multi-file transactions (13, mostly conditional):** one edit across character + profile,
  gameplay + system, or main file + backup. Needs one owner per output and atomic export with
  preflight. Blocking for Gearbox (bank/profile), Pragmata and GoWR writes.
- **F8 Paged/queryable collections (12):** server-side search/filter/sort with stable IDs and
  cursors across unexpanded data. Needed once catalogs reach thousands of rows (Baldur's Gate 3,
  Cyberpunk, Elden Ring, Witcher 3).
- **F9 Map/graph nodes (about 14, optional):**
  - read-only map/marker and prerequisite-graph nodes with a text/list fallback;
  - should be the last thing built;
  - checklists cover the same need first.
- **F10 Exact JSON numbers + nullable values (GenericJson, XorJson):**
  - a numeric value kept as its exact original text;
  - a nullable/union spec that tells null apart from a missing property;
  - UI controls that never round-trip numbers through JavaScript `Number`.
- **F11 Codec routes:**
  - Oodle (AtomicHeart) and Kraken + in-memory SQLite (Hogwarts Legacy via UnrealGvas) need an
    owner decision: a managed reimplementation, an injected host capability, or out of scope.
  - Licensing and the no-native/no-I/O plugin rule are the constraints.

## 3. Suggested sequencing

| Milestone | Contents | Unblocks |
|---|---|---|
| **M0: now, owner** | B3 licensing decision; B1 capture protocol + first sample pairs for 3–4 priority titles; B2 checklist template | Everything |
| **M1: quick wins** | F3 TableView (cells, confidence, live filter, selection); F1 diff tool + fixture manifest | Readable rich views in every plugin; evidence work |
| **M2: reference data** | F2 SDK context + embedded-attribution path, then Core pack import; shared catalog format; F4 picker | Names/icons/pickers in 17 plugins |
| **M3: structure** | F5 stable IDs + reversible structural ops + history; F10 for the JSON plugins | Add/remove item editing; undo everywhere |
| **M4: bulk + review** | F6 preview/apply + semantic diff UI; F7 multi-file where proven necessary | Presets, "give all", respec, trustworthy export |
| **M5: scale + polish** | F8 paged queries; F9 maps/graphs; F11 codec decisions | Large catalogs; map UX; AtomicHeart/Hogwarts |

M1 can start straight away and in parallel with M0. M2's import UI should wait for B3, but the
SDK context design can start before then.

## 4. Per-plugin snapshot

| Plugin | Top blockers (from plan) | First framework dependency |
|---|---|---|
| AstroPlayroom | ID/dependency evidence; newer special-bot samples; PS5 reload test | F3 collection cells, F2 catalogue |
| AtomicHeart | Oodle licensing; inner record grammar unverified; no controlled saves | F11 codec, F1 |
| BaldursGate3 | Differential saves; licensed version-matched data; PS5 gameplay-edit validation | F1 (LSMF decoder), F2, F5 |
| Cyberpunk2077 | One distinct PS5 sample; no versioned TweakDB/localization; gameplay dependencies unverified | F2 (TweakDB), F5, F3 |
| EldenRing | Differential saves + console validation; version-specific dependencies; data/art rights | F2 (SDK + Core), F4 |
| FinalFantasy16 | Licensed versioned data; controlled saves; console validation | F3, F4, F6 |
| FinalFantasy7 | Controlled save pairs + validation; limits/dependencies unverified; licensed icons | F1, F2 |
| Gearbox (incl. BL4) | Data/artwork rights; controlled samples; BL4 PS5 codec/file roles unknown | F5, F7 (character + profile) |
| GenericJson | Exact numbers/nullability; reversible structural edits; controlled evidence (PPSA02343 = *It Takes Two*) | F10, F5 |
| GodOfWarRagnarok | Nested save grammar; unknown integrity/authentication; no controlled saves | F1, F5, F2 (user-supplied PC data) |
| Insomniac | Write acceptance/integrity; inventory not decoded; unverified mappings | F1, F3 (blank lazy explorer cells) |
| Pragmata | No verified ID mapping; no deltas/console validation; no lossless structural serializer | F1, F5 |
| SuckerPunch | Console validation; licensed ID mappings; Yōtei integrity + authoritative records | F2, F3 (confidence) |
| TalesOfArise | Controlled save pairs; licensed ID catalogues; console validation | F2, F4 |
| UnrealGvas | Hogwarts Kraken/SQLite under plugin rules; Jedi/Sackboy checksums; mappings/rights | F11, F1 |
| Witcher3 | Narrow samples; PS5 edit dependencies unverified; data rights | F2, F1 |
| XorJson | One early PS5 snapshot; token editor can't do structural edits; catalog licensing | F5, F4, F10 |

## 5. Decisions needed from the owner

1. **Licensing policy (B3):**
   - CC BY-SA wiki text: may it be embedded, with attribution?
   - Icons: user-supplied packs only?
   - What spoiler default?
2. **Priority titles** for the first round of sample capture (B1). The plans suggest the
   titles where editing is closest: Baldur's Gate 3, Elden Ring, Cyberpunk, Ghost of Tsushima.
3. **Codec scope (F11):** are Oodle/Kraken titles in scope, and under what route?
4. **Overlay vs title plugins:** should GenericJson overlays become dedicated title plugins
   (JSON-R21)?
5. **Tracking:** file F1–F6 as GitHub issues in the issue #1 style, and copy the plans into
   `docs/plans/`?

## Not covered by this round
Borderlands 4 is covered inside the Gearbox plan. Not yet planned:
- Star Ocean;
- Immortals Fenyx Rising;
- Far Cry 6;
- DOOM Eternal;
- Overwatch 2;
- Kena.
