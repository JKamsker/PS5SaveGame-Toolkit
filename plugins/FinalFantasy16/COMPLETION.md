# FinalFantasy16 completion gap audit

2026-10-28, starting main `af414ef`, issue [#15](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/15).
Owner validation/data/captures: [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
The completion brief supersedes earlier disabled-write and framework-gap statements
in the historical plan/comments. The 15 raw write families remain enabled/Experimental.

The unlicensed-source unblock round (2026-10-29) supersedes the former CSV refusal:
the owner authorized community fact imports on 2026-10-28. Five pinned CSVs now
provide 879 item/equipment, 98 map, 1,758 battle NPC and 1,085 event NPC reference
IDs with hashes, source sidecars and explicit attribution. All 41 previous main-ID
name gaps are resolved: actual bytes have **172/172 retail and 2/2 demo** names.
Mapping/category confidence remains Inferred; console validation is still needed.

The game-asset round (2026-10-29) adds 140 icons (92 sample/common item records,
including 36 accessories; all 48 reference abilities), 140 attributed short official
description excerpts and five map backgrounds. Valisthea uses Square Enix's
official art; four regional screenshots are from Game8. Total new image bytes:
**398,116**. Source URLs/hashes and processing notes are in the research media
ledger/sidecar. The saved-location marker stays unpositioned because save-to-art
alignment is Unknown; regional views expand lazily. Publisher-media refusals below
are resolved under the owner's 2026-10-29 decision. No licence blocker remains for
this selection, and no new write semantics or console acceptance is claimed.

| Requirement | Completed / remaining classification |
| --- | --- |
| R1 names | Done for observed naming: 172/172 retail, 2/2 demo, including all 41 former gaps. Published names/categories feed F2/F4/F8; independent PS5 name observations remain an evidence gap, not a licensing blocker. |
| R2 controlled pairs | Owner-blocked: independent inventory, equipment, skill/AP and level events; exact checklist in README/#31. |
| R3 console acceptance | Owner-blocked: spare-slot load/effect/use/save/reload/re-export for each enabled family. Maturity stays Experimental. |
| R4 gameplay rules | Reference AP annotations added. Owner-blocked: matched build/mode/DLC capacity, growth, skill-index/prerequisite/refund and dependency data. No legal-max/refill/respec preset. |
| R5 inventory UI | Done within plugin scope: F3 icons/details/confidence/actions, F8 complete-model queries, unknown-safe F4 identities and catalogue icons. |
| R6 reference choices | Done: F4 on catalogue-backed IDs, preserved original unknowns, native/category bounds. |
| R7 reference context | Done: injected F2 and immutable embedded fallback; no runtime file/network I/O. |
| R8 evidence tools | Done: corpus/schema/range metadata, F1 fixture, deterministic converters, format/sample tests and the native F12 `ffxvi-evidence` decoder hook. |
| R9 compatibility | Measured triples and retail/demo/empty/system/legacy/extended coverage retained. Owner-blocked: additional region/build/mode/DLC captures before expanding schemas. |
| R10 allocation | Owner-blocked: create/remove/reacquire/slot reuse/capacity/dependency pairs. No safe structural writer exists yet. |
| R11 candidate validation | Done for raw edits: atomic validation, identity swaps/duplicate rejection and rollback. Owner-blocked: gameplay dependencies require semantic data. |
| R12 previews | Done: F6 deterministic revalidation, direct/derived changes, warnings, skipped rows and exact CRC. |
| R13 selection | Done: explicit/filtered/excluded scopes across unloaded pages; refresh after edit/undo and exact group history tested. |
| R14 structural representation | F5 available. Owner-blocked on R10 format evidence before a safe writer can consume it. |
| R15 structural history | F5 available. Owner-blocked on R10; fixed-width/bulk value history tested. |
| R16 gear/ability UX | Added tree/AP context; category-aware F4 gear/command/shortcut choices retained. Owner-blocked: active banks, slot/build/ownership/mastery eligibility and automatic AP/stat reconciliation. |
| R17 progress references | Unknown/raw explorer retained. Owner-blocked: named quest/obelisk/collection keys, dependencies and transforms require matched data/one-event captures. |
| R18 matched XML | Owner-blocked: legally obtained matched binary/XML states could establish associations; optional research input, not console validation. |
| R19 licence policy | Done: CC BY-SA/MIT, owner-approved published CSV and publisher media provenance, hashes, notices and Sources panel. Icons, short official text and map art imported under the 2026-10-29 owner decision. Save-to-art transforms remain an evidence gap. |
| R20 progress transitions | Owner-blocked: independent upgrade/reward/quest/obelisk/chest/letter/orchestrion/DLC unlock captures. |
| R21 tree/map/checklist | Done where supported: searchable/filterable reference graph, Unknown totals and spoiler gate; candidate-location map/text list/guide. Owner-blocked: meaningful completion checklists and positioned collectible pins need named save mappings. |
| R22 coordination | N/A: no substantiated coupled-file dependency. Won't do speculative system rewrites. |

Acceptance completed within code scope: small composed classes, no-op/unknown-byte
preservation, write gates/ranges, attribution, paged inventory and existing raw-edit/F6
history. Unsupported schema variants remain preserved/read-only for that feature.
All 72 archive occurrences remain part of preservation checks. Unknown 900000,
zero stacks, independent flag arrays and original outliers remain intact.

Owner-blocked acceptance: controlled name/bank/skill/growth
matches, legal contextual caps, console acceptance, meaningful progress completion
and safe allocator semantics. Full story/NG+/DLC unlock mutations and structural
mutation stay excluded until a safe dependency model exists. Test/CRC success is
never console evidence. New labels, costs and diagrams are reference data only.

Won't do: manufacture ID/rank/quest associations or map transforms; assume
companion-file coupling; add unrelated platform wrappers.
F12/F13 are consumed from merged framework support: the native `ffxvi-evidence` decoder hook
feeds bounded SaveDiff traversal, and the catalogue lookup is a read-only F13 inspect
action with browse-only read-only ID pickers.

Agent work complete; remaining items are owner-blocked (#31), with F12/F13 adoption
recorded above. Final unblock-round plugin validation: 122 passed,
0 failed, 0 skipped with samples configured; API Release build 0 warnings/errors;
F1 fixture 1 passed, 0 failed/skipped. No files outside this plugin were changed.
