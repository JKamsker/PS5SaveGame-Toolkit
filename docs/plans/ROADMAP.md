# PS5 Save Toolkit: cross-plugin requirements roadmap

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
