# Completion audit — issue #20

Baseline: main `1f0d1f1`, 2026-10-28. Prior handoffs through `ee52a06` reviewed.
Branch: `plugin/insomniac-completion`. No push or merge.
The completion/write briefs override historical read-only gates in the plan.

| Requirement | Final status / classification of remaining work |
| --- | --- |
| R1 | **Owner-blocked:** controlled bolt/purchase/option calibration; writes already enabled with warnings. |
| R2 | **Delivered:** F1 decoder, ordinal/generation/twin evidence and atom diff tests, including the new F6 action over all 36 primary records. |
| R3 | Five attributed catalogs delivered. **Owner-blocked:** build/locale configs, gameplay limits and identity joins absent from the corpus. |
| R4 | **Owner-blocked:** console load/effect/native re-save/reload/recovery and Stable decision (#31). |
| R5 | **Delivered:** F3 cells/confidence, F9 navigation targets and lazy F8 diagram/detail expansion through actual Core sessions. |
| R6 | **Owner-blocked:** isolated weapon/acquisition/XP/ammo/raritanium/Omega/armour captures and legal caps. |
| R7 | **Owner-blocked:** exact pickup joins, completion and Challenge reset pairs. |
| R8, R9 | Delivered centrally through F2; plugin resolves immutable catalogs with fallbacks. |
| R10 | **Delivered:** native F9 upgrade-state graphs and external-link collectible map with explicit text alternatives and field targets. No invented prerequisite edges. |
| R11 | F3/F4/F8 inspection delivered. **Owner-blocked:** ownership-aware equipment choices and costs. |
| R12 | **Delivered:** external-only map/list of actual storage identities. **Owner-blocked:** physical coordinates, mission instance joins, calibrated transforms. |
| R13 | F5 available centrally. **Won't do:** insert/remove records without a lossless constructor, ownership and dependency repair; no speculative copy-record implementation. |
| R14 | **Delivered:** F6 bounded bolt-target operation over the existing career counter, including derived balance/expanded diagnostics, warnings, no-ops, stale/forged-plan checks and Core undo/redo. **Owner-blocked:** ammo/loadout/upgrade recipes and legal caps. |
| R15 | **Delivered:** source sidecars, notices, README, Info.References and UI provenance, including F9 links/original graph presentation; no unlicensed art/coordinates. |
| R16 | **Delivered:** 111 passing tests cover corruption, negative counters/masks, optional shapes, exact UInt64 transport, bounded ASCII, isolated exports, corpus/width/byte preservation, F9 bounds/navigation, F6 atomicity and real Core history. |
| R17 | **Owner-blocked:** differing known builds/regions and optional permitted PC/config captures; support remains PPSA01474. |
| R18 | **Delivered:** bounded F9 marker/text navigation and external guide link. **Owner-blocked:** exact fog transforms, physical landmarks and map-instance semantics. |

Acceptance audit: plugin-only scope; 76-record no-op preservation; bounded recursive
parsing; useful preferences/loadout; provenance; fixed-capacity writes; atomic
edit/reopen/undo; generation/backup preservation; test quality and accurate docs are
**delivered and verified**. Ownership proof, HUD-calibrated values/caps, mode
transitions, physical map alignment and successful console effects are
**owner-blocked** under #31. No UI/framework files will be edited; browser renderer
accessibility is centrally owned and plugin contracts are checked here.

Each acceptance bullet in plan order:

| Criterion | Final disposition |
| --- | --- |
| A1 Scope/style | Delivered: plugin folder only; every non-generated C# file below 300 lines; no tooling/hooks. |
| A2 Preservation | Delivered: all 76 supplied records byte-identical without edits, including generations/twins. |
| A3 Bounded readers | Delivered: existing recursive budgets/bounds retained; corruption plus diagnostic-shape coverage expanded. |
| A4 Quick-select/preferences | Delivered with attribution and honest interpretation labels. |
| A5 Inventory ownership | Framing/identity/state inspection delivered; **owner-blocked** controlled ownership/equip/XP/ammo joins (captures 1–5). |
| A6 Named joins/provenance | Delivered for inferred catalogs and unknown fallbacks; **owner-blocked** independent build/localization validation (capture 6). |
| A7 HUD/limits/modes | Observed/type-bounded enabled writes preserve imported outliers; **owner-blocked** HUD/legal-cap/mode calibration. |
| A8 Existing capacity | Delivered: only fixed-width existing atoms; **won't do** relocation/width promotion without a safe serializer. |
| A9 Persistence/invariants/undo | Delivered: enabled writes and F6 action reparse, persist, preserve unrelated bytes and undo; known nonnegative-balance invariant enforced. Unknown integrity is explicitly warned. |
| A10 Safe actions | Delivered: document-wide bounded bolt target; **owner-blocked** ownership/legal-cap/equipment/upgrade recipes. No unsafe completion/entitlement operation. |
| A11 Export identities/backups | Existing Core policy retained; plugin returns the unchanged relative path and isolated validated bytes; no new paths/group assumptions. |
| A12 Tests | Delivered: edit widths, corruption/unknown shapes and byte preservation; **owner-blocked** additional real mode-transition captures. |
| A13 UI/maps | Plugin Core contracts tested for F3/F4/F8/F9, bounds, targets, text alternatives and absent art; renderer accessibility is centrally owned. **Owner-blocked** three-landmark physical calibration; no transform claimed. |
| A14 PS5 acceptance | **Owner-blocked:** exact #31 load/effect/re-save/reload/re-export/recovery checklist. Experimental; only owner sets Stable. |
| A15 Docs/data | Delivered: current features, evidence/writes table, source notices, feature adoption, exact captures and no private bytes/assets/account data. |

Unsafe features deliberately excluded: record creation/removal, string relocation,
XP/progression/raritanium correction, equipment/grants, paid entitlements, blanket
completion and Challenge conversion. The necessary constructors/dependency models
do not exist; warnings alone cannot make those operations data preserving. Required
research captures are in OWNER-CAPTURES.md and #31.

## Round result

All do-now items are implemented. Phase 1 and Phase 2 inspection remain delivered;
Phase 3 scalar writes stay enabled/Experimental; Phase 4 gains bounded F6 targeting
and F9 storage presentation. Owner-dependent gameplay/physical joins are not claimed.
Phase 5 speculative structural/state-machine work is deliberately excluded above.

**111 passed, 0 failed, 0 skipped** with SAVETOOLKIT_ARTIFACTS assigned in the same
test invocation; all 76 no-op records preserved. API build: **0 warnings/errors**.
F4 references and F8 raw inventory paging are expanded. Repeated schema loads,
redundant serialization/ASCII copies, unbounded ASCII previews and diagnostic
cardinality/negative-value errors are addressed. All C# files remain below 300 lines.

Framework adoption: F1/F2/F3/F4/F8/F10 used, F4/F8 expanded, F6/F9 added; F5 lacks a
safe structural writer, F7 has no identified cross-file invariant, F11 has no
compressed/SQLite payload. The F12/F13 follow-up adopts the production evidence hook,
browse-only references, shared filter/sort across all five catalogs and float Playtime
duration hints. Inspection actions and planned reference parameters are N/A here.
See README's full adoption table. No SDK bug/request blocks the delivered work.
F12/F13 follow-up validation: **124 passed, 0 failed, 0 skipped**; API build:
**0 warnings/errors**. CLI hook discovery and sample comparisons pass. All changes
remain under `plugins/Insomniac/`.

Owner queue [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): existing
scalar acceptance plus F6 balance target and F9 join experiments, exact numbered
protocol in OWNER-CAPTURES.md (1–21). Preserve build/region/slot/mode/DLC/character/
locale and full private BEFORE/AFTER/AFTER-RELOAD controls/diffs. Pristine recovery,
load/effect/native-save/reload/re-export, independent bolt/spent/option/preference
trials, acquisition/XP/ammo/raritanium/Omega/equipment/collectible/Challenge pairs,
per-node cost/prerequisite captures and three non-collinear landmarks per actual
physical transform remain human work. No console acceptance has been established.

Attribution: existing CC0/CC BY-SA catalogs retained; new SPATIAL-SOURCES.md,
README/Info.References/UI attribution for the link-only Guides4Gamers map index;
original upgrade-mask descriptions with CC0 schema attribution. No art/coordinates
or downloaded upstream implementation imported. Nothing outside the plugin changed.

Agent work complete; remaining items are owner-blocked (#31).
