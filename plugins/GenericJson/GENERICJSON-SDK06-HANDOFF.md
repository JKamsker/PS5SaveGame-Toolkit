# GenericJson SDK-06 handoff — 2026-10-29

## Immutable branch/dependency

- Consumer branch: `t3/genericjson-sdk06-adoption`
- Consumer base before dependency merge: `e76e51035fec308a56e5c26b5d97a9c08089f881`
- Original R1 fix head: `c78fa498` (`fix(genericjson): preflight scalar output size before encoding`)
- Reviewed dependency: SDK-06 PR #51, exact commit
  `72490fe8756627013c73c0b80fa874b563a0e652` (parent `a466a4b16ef271eb98e30f784cfcaba03e8654ae`)
- Dependency branch/base: `t3/sdk06-lossless-json` / `e76e51035fec308a56e5c26b5d97a9c08089f881`
- Consumer dependency merge: `5e452662` (ordinary `--no-ff` merge; no cherry-pick/rebase/squash)
- Consumer PR target: `main`. PR #51 merged; ordinary consumer merges retain SDK06 and published
  main `201dd690a48a93bf496f1825246bc6954cc5ec52`. Parent owns fresh review and hosted validation.
- Consumer PR: https://github.com/JKamsker/PS5SaveGame-Toolkit/pull/62
- Immutable dependency-to-consumer diff contains **11 changed paths**, all under `plugins/GenericJson`.
  The earlier 12-file note was incorrect and is superseded by this count.

## Actual adoption

`JsonTokenTree.Parse(byte[])` now calls `LosslessJsonDocument.Parse` with GenericJson's exact
16 MiB/64-depth/250,000-value limits. The mutable `JsonValueToken` model adapts the shared
preorder IDs, parent/children, decoded names, duplicate occurrence metadata, exact property-name
spans and typed scalar metadata. Oversized valid numeric lexemes still become local read-only raw
text, while normal exact numbers retain `ExactJsonNumber` validation.

For unchanged topology and imported member names, `JsonScalarPatches` uses the SDK document's
`ReplaceScalar` and `Serialize`. It captures/restores a document-owned SDK snapshot for temporary
export, applies shrinking replacements first, and preflights the complete output budget. Shared
source state is restored after export, so sibling plugin snapshots cannot affect one another.

The streaming validator remains only for low-allocation `Probe`, structural round-trip equality,
and mapping shared parse failures to GenericJson's established diagnostic precedence. GenericJson's
local writer remains authoritative for object/array insertion, removal, reorder, duplicate/name
handling, punctuation/trivia, stable `value/{Index}` identities, structural snapshots, title
overlays, reference pickers, action envelopes and warning/confidence semantics. No working
structural feature was removed and no SDK source was copied into the plugin.

## Preservation and robustness evidence

- New combined history golden covers escaped duplicate members, oversized numeric fallback,
  inserted-node source isolation, scalar edits after insertion, removal, exact-byte undo/redo and
  stable IDs: `JsonSubstrateHistoryTests`.
- New shared-source snapshot golden proves two plugin trees can export different scalar edits and
  then restore the untouched original bytes: `JsonSubstrateHistoryTests`.
- Exact-MaxBytes batch golden covers a growing multibyte/string replacement paired with a shrinking
  replacement at the exact 16 MiB boundary, then restores imported bytes: `JsonResourceTests`.
- Existing suites continue to cover BOM/trivia, duplicate names and raw escapes, exact integers/
  decimals/exponents, null/missing distinctions, malformed UTF-8/Unicode/grammar, limits,
  structural insert/remove/move/duplicate/rename, stale IDs, title adapters, F4/F6/F8/F9 actions,
  conformance and mutation fuzzing.

Release plugin validation after adoption: **458 total, 425 passed, 33 missing-sample skips,
0 failed**. Missing samples are the existing external PPSA archives and skip explicitly; synthetic
mutation fuzzing ran. The SDK-06-specific focused tests and full plugin suite were run with all
build/cache/results under `D:/Temp/saveeditor/test-scratch/sdk-full-roadmap-20261029/genericjson-sdk06`.
The opening allocation guard is 52 MiB because the adapter intentionally retains the reviewed SDK
immutable lexical tree alongside the plugin mutable structural model; no correctness guard was
removed. No private samples or values were read or copied.

## Remaining limits

SDK-06 has no structural API, so structural trivia and history remain plugin-owned. Shared scalar
patching is unavailable after topology/name changes and correctly falls back to the local writer.
Title writes remain experimental, with existing Inferred/Unknown labels and external game-integrity
warnings. The foundation/main gate is satisfied; fresh independent consumer review and hosted
combined-source validation remain parent-owned landing requirements.

## PR62-R1 response

Review `PR62-R1-01` identified one P1: the adapter encoded every changed scalar and retained all
replacement byte arrays before checking the complete output budget. Fixed in `c78fa498`. The
adapter now computes `JsonScalar.EncodedLength` for every changed scalar first, using a nonallocating
counter for JSON quotes, control escapes, default HTML-sensitive escapes, non-ASCII UTF-16 escapes,
surrogate pairs, exact numbers, booleans and null. Checked cumulative deltas include the original
BOM/trivia and every current replacement. Only a passing batch is sorted shrink-first and then
encoded/applied under the existing source lock and SDK snapshot `finally` restore.

Added `JsonResourceTests.ScalarLengthPreflightRejectsEscapedGrowthBeforeReplacementAllocation`:
a valid near-16 MiB multibyte string whose escaped output would be roughly 96 MiB is rejected with
an allocation assertion of at most 10 MiB, unchanged bytes, unchanged typed value and no undo
history. `ScalarLengthPreflightMatchesDefaultJsonEscaping` covers non-ASCII, surrogate-pair,
HTML-sensitive and control escapes. Final-fitting grow/shrink order coverage remains in the exact
16 MiB boundary test. No other R1 finding was reported; dependency remains exact SDK06 `72490fe…`.

Post-fix plugin validation: **463 total, 430 passed, 33 explicit missing-sample skips, 0 failed**;
synthetic mutation fuzzing passed. The API Release build and documentation checks were rerun after
the final follow-up commit; all build/cache/results remain under the assigned D: scratch root.

## PR62-R3 response

`PR62-R3-01` found an incomplete counter: the unchanged default serializer hex-escapes quote,
plus and backtick to six bytes each. Their old two/one/one-byte counts permitted oversized
replacement allocation and misordered a final-fitting atomic batch. The counter now uses six
for all three; backslash remains two. Serializer options, output bytes, limits, checked aggregate
accounting, source lock, SDK snapshot restoration and structural/history paths are unchanged.

The new runtime serializer oracle covers every ASCII code point and mixed Unicode including
controls, HTML-sensitive characters, BMP boundaries and supplementary pairs. Before the fix,
the oracle failed at U+0022 and the exact-limit grow/shrink batch failed in both edit orders.
After the fix, all 24 resource tests pass. Near-limit plus/backtick strings now join the existing
non-ASCII allocation regression: rejection stays within the unchanged 10 MiB measured bound,
with original bytes/value/revision and no undo/redo history. At exactly 16 MiB, replacing
`["aaaaaaaaaa","bbbb"]` with decoded `["++","bb"]` succeeds in either edit order and emits
the original default `\u002B` escapes, with byte-exact undo/redo and stable IDs.

Current Release plugin evidence: **471 total, 438 passed, 33 explicit sample skips, 0 failed**;
synthetic mutation fuzzing and API Release build pass. R1's preflight-before-encoding ordering
closure and the reviewed SDK foundation fixes remain intact; R3 corrects the incomplete encoder
coverage missed by R2. Fresh R4 and hosted checks are still required. The historical full-Core
run at `736ded89` ended with runner exit -1 and is not a passing suite; no cause fix is claimed.
