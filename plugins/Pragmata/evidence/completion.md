# Completion audit — 2026-10-28

Base: main `ab748cf` including F13. Issue #21 and all roadmap/write handoffs were
reviewed. Historical round-2 statements describe that round; the current README
and this audit supersede its read-only fields and unsupported inventory-query claims.
The later [community-source unblock](unlicensed.md) restores five names and adds
459 schema declarations plus 230 enum values with attribution; historical research
notes here do not impose a licence-only exclusion.

| Requirement | Final status | Evidence / remaining condition |
| --- | --- | --- |
| R1 versioned reference manifest | Agent work complete; owner-blocked mapping | 408 identifier names, 27 sections, 90 explicit Unknown item placeholders, six PC item-role links, now 43 attributed PC mission references. Localized ID/name/category mapping still needs an extraction with matching IDs. |
| R2 legal limits/dependencies | Owner-blocked #31 | Typed fixed-at-open conservative bounds and warnings exist; float-range initialization corrected. Storage/observed bounds are not claimed legal caps. |
| R3 assets | Agent work complete; owner data absent | Attributed F2 user-pack images already display in item details. No licensed ID-linked icon corpus found; do not invent one. |
| R4 causal pairs | Owner-blocked #31 | Three independent one-event pairs per family and unchanged controls, including siblings. |
| R5 broad corpus | Owner-blocked #31 | Actual empty/full/version/mode saves absent; synthetic 251-row inventory exercises software scale without claiming a real game layout. |
| R6 runtime capture | Owner-blocked #31 | Runtime resolver API documented; owner must supply game access/extraction. |
| R7 console/Stable | Owner-blocked #31 | Remains Experimental; unchanged, quantity, ID and five-family console tests outstanding. |
| R8 diff harness | Agent work complete; F12 adopted | Production SDK canonical hook selected by stock savediff, shared bounded F8 inventory traversal, research executable removed; all-sample quantity/family deltas retained. |
| R9 malformed fixtures | Agent work complete for fixed-size support | Existing truncation, alignment, metadata/hash corruption plus duplicate roots/fields, nesting and oversized array rejection. Structural corpus needs layout-changing captures. |
| R10 reference context | Agent work complete | F2 pinned packs and attribution; F13 browse-only catalogue search/category/sort tested. |
| R11 export/groups/diff | Agent work complete for supported operations | Serialize decrypts/reparses; F6 semantic review and single-file export. Coupled-file behavior remains owner-blocked; no proven dependency to claim via F7. |
| R12 structural writes | Won't do in this round | No lossless resizing writer or layout-changing corpus. Unknown counts/alignment/padding must survive; fixed-size identity does not prove that. Future structural implementation requires #31 captures. |
| R13 pickers/grid/bulk | Agent work complete | F3/F4 retained, F8 inventory above 200, #27 query resolution and exclusions; complete 251-row semantic review, binding preflight, group undo/reopen/byte-isolation regression. |
| R14 graph/map/quests | Agent work complete for supported evidence | F9 item-to-mission DAG, Unknown states, navigable browse-only fields; bounded checkpoint map/list with navigable existing values and external map. No guessed coordinates or completion states. |
| R15 embedding policy | Complete | Attributed MIT/CC0 packs; owner permits compatible data. No extra approval required. |
| R16 RSA/new flags/outer layer | Owner-blocked / unsupported | Preserve unknown RSA trailer and warning; reject unsupported flags. No documented signing/outer algorithm to implement. |

Acceptance criteria: all-12 byte-identical no-ops, intended fixed-size byte ranges,
edit/reopen/reverse, atomic invalid-value rejection, aliases, attribution, bounded
queries and Experimental maturity remain covered. Causal gameplay claims/legal
caps and console acceptance require #31. Strings/structs/arrays/padding/metadata
are preserved in unchanged layouts, with no structural mutation claim. Production
still depends only on SDK and performs no I/O. No shared files were edited.

## Research

The pinned [MIT mission data](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json)
contains 43 units but no resolved localized strings: message GUIDs remain GUIDs.
All units now have an attributed F2 catalogue record; no reward count is used as
an inventory cap. The six exact PS5-inventory overlaps retain only their Inferred
PC opening-condition role. Runtime sources still resolve names at runtime rather
than shipping a static ID dictionary; public repository/web searches found no
additional licensed exact-ID/localized-name/cap dataset suitable to embed.

The [itemaps community map](https://itemaps.com/maps/pragmata) was inspected as
an external map reference (itemaps contributors; retrieved 2026-10-28; licence
unverified). No artwork, coordinates, marker text or totals were imported.
Checkpoint labels come only from stored hashes and the MIT identifier spelling.
Every marker remains Unknown and unpositioned. Its field target is independent
of page ordering, and recursive navigation follows the same bounded paging policy.

Downloads remain under `%TEMP%/pragmata-completion`; only SHA-pinned JSON inputs
were processed with the local generator using `python -I`. No downloaded code
was executed. Pack 1.2.0 has sidecar, full MIT notice, README/Info.References,
visible provenance and Sources attribution.

## Owner checklist additions

Retain every exact capture/console item in [the evidence README](README.md#scope-and-owner-checklist)
and #31. Also record each checkpoint hash before/after a single hatch/checkpoint
unlock and its actual location; capture mission selectability and chapter/runtime
prerequisites separately from the six opening-condition item quantities. Graph
edges are reference evidence, not a claim that editing an item grants a mission.
For a real inventory over 200, exercise filtered all-matching selection with one
exclusion, preview/apply, one undo/redo, export/re-import and console reload; verify
every intended quantity and that the exclusion remains unchanged.

Agent work complete; remaining items are owner-blocked (#31), with structural
rewriting explicitly deferred. The F12 follow-up adopts the merged SDK hook and
shared bounded traversal from #32.

## Final software validation

- F12 follow-up: **132 passed, 0 failed, 0 skipped** with the external samples
  environment in the test command; API build **0 warnings, 0 errors**. Stock CLI
  discovery and a real cross-slot comparison select the production canonical hook
  successfully. F8 inventory paging, last-page edits and shared row/page/byte
  caps are covered. Positional identity uncertainty is now explicitly incomplete.
- Plugin suite with `SAVETOOLKIT_ARTIFACTS` exported in the same command:
  **120 passed, 0 failed, 0 skipped**, covering all 12 supplied samples.
- API build: **0 warnings, 0 errors**.
- Pack 1.2.0 regeneration from both SHA-pinned inputs: byte-identical.
- Staged whitespace check clean; every C# file below 300 lines. No browser/GUI
  validation is claimed. All changed files stay inside `plugins/Pragmata`.
