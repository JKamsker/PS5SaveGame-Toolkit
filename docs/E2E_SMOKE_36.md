# End-to-end smoke pass (#36)

Run on 2026-10-28/29 on `framework/e2e-smoke`. Tracking and reproductions:
[issue #36](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/36).

The API and built React app ran together at `http://127.0.0.1:5280` from this
worktree. The owner's port 5180 was left alone. Samples were read directly from
`SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`.
Imported workspaces and temporary drivers/results lived outside the repository.
No plugin files or source samples were modified.

## Scope and evidence

- Imported all 33 `PPSA*_saves_dec*.zip` archives; excluded the separate community
  editor toolkit archive. Titles without a matching decoder still received the
  whole-workspace export check.
- Attempted all 563 non-raw plugin matches, including backups and alternate
  matches. Opened 539 documents successfully. All 539 no-op downloads matched
  their original source bytes.
- Exported all 33 complete workspaces. The multiset of SHA-256 hashes of exported
  payloads matched all 2,129 original non-directory ZIP entries. This compares
  file content, not ZIP compression bytes or container ordering.
- Traversed eager nodes and up to 200 distinct lazy nodes per document: 84,951
  expansions. Queried discovered F8 tables with paging, text search and sorting;
  queried published reference fields and ran discovered non-mutating inspections.
- Completed 28 browser walks / 211 tab visits across all 17 plugins, including
  largest matched representatives, additional game profiles and both FF16 titles.
  Insomniac Systems was excluded from later walks after reproducing its failure.
- Representative scalar edits, changes review, undo/redo/undo and exact byte
  restoration passed for 15 plugins. GenericJson instead used a structural
  insertion; XorJson used a character/global grouped variant-value edit.
- Browser-specific checks passed for Astro F8 search/filter/sort/paging/select-all;
  Astro maps and Tales graphs with keyboard pan and text alternatives; attributed
  external links; browse-only picker Escape/focus restoration; FF7 editable picker
  within a planned action; Astro and FF7 preview/apply/undo/redo; JSON structural
  preview/apply/undo/redo; Gearbox and XorJson group proposals/open/edit/undo.
- XorJson ChangedFiles export staged both selected members. Final undo restored
  every whole-workspace exported payload. Group single-file download was hidden.

## Results per plugin

Opened counts include primary/backup copies and sample generations. Every opened
document passed its no-op byte comparison. Tests have zero skips throughout.

| Plugin | Opened | Tests passed / total | Result |
|---|---:|---:|---|
| AstroPlayroom | 4 | 69/71 | UI flows pass; two semantic diff expectations fail (A). |
| AtomicHeart | 2 | 59/59 | OK within the bounded pass. |
| BaldursGate3 | 12 | 168/168 | UI/edit/export pass; cold Story inspection exceeds 2 s (B). |
| Cyberpunk2077 | 2 | 145/145 | OK; changes review explicitly reports incomplete coverage. |
| EldenRing | 2 | 214/214 | OK within the bounded pass. |
| FinalFantasy16 | 72 | 92/92 | Retail/demo flows pass; read-only lookup annotation gap (C). |
| FinalFantasy7 | 55 | 121/121 | Remake/Rebirth flows pass; bounded changes review is incomplete. |
| Gearbox | 24 | 125/125 | Both games and character/profile group pass. |
| GenericJson | 13 | 180/180 | Structural preview/apply/history and exports pass. |
| GodOfWarRagnarok | 156 | 81/81 | OK; bounded changes review is incomplete. |
| Insomniac | 76 | 111/111 | API passes; Systems auto-expansion overwhelms browser (D). |
| Pragmata | 12 | 120/120 | OK within the bounded pass. |
| SuckerPunch | 33 | 126/126 | Tsushima and Yotei views/exports pass within published capabilities. |
| TalesOfArise | 6 | 66/67 | UI/edit/export pass; semantic decoder truncation assertion fails (E). |
| UnrealGvas | 60 | 85/85 | Normal supported titles pass after framework fix; 24 incompatible alternate Gearbox matches reject (F). |
| Witcher3 | 8 | 108/108 | OK within the bounded pass. |
| XorJson | 2 | 70/70 | Character/global group, variants, queries and export/history pass. |

## Findings and reproductions

The framework fix is `c1762df`: opaque collection IDs now travel in
`GET /collections?collectionId=...`. Previously selecting Sackboy's **Explore all
properties** called `/collections/gvas%2Fproperties`; Kestrel retained `%2F` in
the route parameter and the API returned 404. The legacy route remains available.
API and client regressions cover slash, literal `%2F`, reserved characters, query
limits and stale revisions. No SDK or shared protocol type changed.

Plugin-owned findings were posted on #36 without changing plugin source:

- **A — Astro:** with samples configured, run its test project. Differential
  evidence expects 94 semantic changes but gets 107; collect-all expects 164 but
  gets 328. Earlier byte-output assertions pass. Expanded read-only/queryable
  projections appear to require reconciliation with the semantic expectations.
- **B — BG3:** import PPSA18463, open `AutoSave_0.lsv`, first expand **Story →
  Story and approval database markers** (`story/databases`). Primary/backup first
  responses measured 6.426/6.370 s; a subsequent already-scanned expansion was
  31 ms. `StoryInspection.Scan` is plugin-owned lazy marker scanning.
- **C — FF16:** import PPSA10665, open `FAITHDATASYSTEM/system-save-data`, select
  **Inventory**. **Look up an item** is disabled on the read-only document.
  `FaithInventoryView` omits `Mutates = false` on this inspection-only action.
- **D — Insomniac:** import PPSA01474, open the largest `i29-s.save` in snapshot
  `108976056`, select **Systems**. Default-open lazy descendants recursively load;
  observed 177,836 field elements, 1,440 loading sections and
  `net::ERR_INSUFFICIENT_RESOURCES`. `DdlTreeView` lazy sections and Systems roots
  should publish collapsed presentation to require explicit expansion.
- **E — Tales:** run its sample test project. `AriseDiffTests` line 35 expects
  `tree.Truncated == false`; the expanded shared traversal reports true with the
  current limits. Reconcile the expanded surface/budgets and test expectation.
- **F — UnrealGvas:** import PPSA01463 or PPSA01493, choose file `1`, explicitly
  switch to Unreal Engine GVAS. Its magic-based alternate probe offers the
  incompatible Gearbox variant; Open returns 422 (GVAS length/header error).
  Default higher-priority Gearbox opening succeeds for every such file.

## Validation and limits

`dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj`: zero warnings/errors.
`dotnet test --project tests/SaveToolkit.Core.Tests`: 309 passed.
`./scripts/test.ps1` with samples: 2,381/2,384 passed across 20 projects; three
failures described above, zero skipped. Codecs 25/25 and SaveDiff 107/107 pass.
`npm install`, `npm run build`, `npm test` in `web`: build passes, 168 tests pass
across 29 files. `git diff --check` passes.

Maximum measured import/open times were 1.971/1.139 s. The two BG3 first-scan
responses were the only >2 s lazy calls recorded. Browser walk timing includes
short settling delays and is not a formal render benchmark. No 409 loops,
`[object Object]` rendering or visible accessibility blocker appeared in the
completed representative flows. The Insomniac failure is the recorded exception.

This is bounded functional coverage, not exhaustive exploration of every lazy
leaf, every table row, every picker candidate or every planned-action parameter.
Several large documents honestly mark semantic changes coverage incomplete.
Actual read-only sample documents with published non-mutating inspections were
not available in this corpus; FF16 exposed the annotation gap above. Framework
read-only inspection regression tests pass. No new browser dependency was added.

Acceptance: the per-plugin issue table is complete; the discovered framework
bug is fixed with regressions; plugin findings have minimal reproductions.
The full-suite-green goal remains unmet because of A/E. Plugin owners must also
resolve B/C/D/F; re-run those paths afterward. Issue #36 remains open.
