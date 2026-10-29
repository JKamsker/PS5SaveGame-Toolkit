# GenericJson SDK-06 handoff — 2026-10-29

## Immutable branch/dependency

- Consumer branch: `t3/genericjson-sdk06-adoption`
- Consumer base before dependency merge: `e76e51035fec308a56e5c26b5d97a9c08089f881`
- Consumer implementation head: `ee2b5a6c` (`feat(genericjson): adopt SDK06 lossless scalar substrate`)
- Reviewed dependency: SDK-06 PR #51, exact commit
  `72490fe8756627013c73c0b80fa874b563a0e652` (parent `a466a4b16ef271eb98e30f784cfcaba03e8654ae`)
- Dependency branch/base: `t3/sdk06-lossless-json` / `e76e51035fec308a56e5c26b5d97a9c08089f881`
- Consumer dependency merge: `5e452662` (ordinary `--no-ff` merge; no cherry-pick/rebase/squash)
- Consumer PR target: `t3/sdk06-lossless-json` while PR #51 is pending main; parent must retarget to
  `main` after PR #51 lands and rerun CI. Do not merge this consumer before that foundation.
- Consumer PR: https://github.com/JKamsker/PS5SaveGame-Toolkit/pull/62

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
warnings. PR #51's hosted Windows CI and eventual main merge remain parent-owned; after merge,
retarget this consumer PR and rerun the full required checks.
