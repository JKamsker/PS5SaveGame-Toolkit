# GenericJson SDK-06 readiness — 2026-10-29

Baseline: `e76e51035fec308a56e5c26b5d97a9c08089f881`; assigned branch:
`t3/genericjson-sdk06-adoption`. This is a compatibility inventory, not an adoption claim.

## Existing format and lexical contract

- Plain UTF-8 JSON, optional leading UTF-8 BOM; all root scalar/container kinds. Strict
  whole-input UTF-8 validation precedes streaming JSON/name/string decoding. Invalid UTF-8,
  unpaired escaped surrogates, comments, trailing commas, multiple roots and incomplete input
  reject. Preserve existing diagnostic ordering and numeric-only error locations.
- Bounds: 1 byte–16 MiB inclusive (BOM/trailing whitespace count), 250,000 **values**
  including containers (names/end tokens do not count), depth 64. Exact-boundary tests exist.
- Preorder nodes retain original source byte offsets/lengths, ordered children, decoded member
  names, quoted member-name spans and name-to-value colon/trivia spans. Duplicate decoded names
  remain separate nodes with occurrence numbers, including differently escaped equal names.
- Every number retains its exact lexeme: large integers, decimals, signed zero and exponents.
  Syntax-valid numbers outside `ExactJsonNumber` length/exponent budgets remain verbatim,
  read-only text; other values in that document remain editable. No floating-point conversion.
- No-op and revert-to-imported-value serialization preserve every byte: BOM, surrounding
  whitespace, original string/name escaping, numeric spelling and delimiters. Edited strings
  use the existing `JsonSerializer` encoding. Null, absent members and empty strings differ.

## Migration boundaries

Replace the pure lexical `ReadTokens`/`CreateNode`/`InitializeSpans` implementation with an
adapter over the shared immutable lossless tree; use shared scalar patching where topology
and member names remain original. Keep plugin-owned mutable nodes, immutable session IDs,
duplicate occurrence/path rules, original-child identity lists, source retention limits,
structural writer/validation, snapshot history and monotonic ID allocation. Structural edits
must continue after insertion/removal/rename/reorder and across undo/redo; never rebuild IDs
from freshly parsed output or use an original source offset as a durable identity.

Title matching stays local: Cyberpunk `PPSA04027/settings.json` version 136/data group shape;
It Takes Two `PPSA02343/ue4savegame.dpx.sav` nested string Profile plus CloudSettings and
LocalSettings; Ridge Racer `PPSA07696/imgui_emu_option.json` options/settings objects.
Duplicate required members invalidate unique-member matches. No title gameplay changes.
Reference parameters, planned actions (F6), current single-file behavior (F7 assessment),
full-tree/array queries (F8), profile text map (F9), decoder paths and canonical IDs stay intact.

## Minimum shared API requested

- Parse strict UTF-8 bytes with explicit byte/value/depth limits and optional BOM support.
- Read-only preorder nodes with token kind, source offset/length, decoded property name,
  quoted property offset/length, scalar string/bool/raw-number access, parent and child order.
  Enough source boundaries to retain leading/trailing/separator/name-colon trivia exactly.
- Span-preserving scalar replacement addressed by token identity/index, including changes in
  encoded length and explicit scalar type; preserve untouched spans and enforce output bounds.
- Raw syntactic number support independent of editable `ExactJsonNumber` resource budgets.
- No structural/history/public UI policy forced into the substrate. Error adapters must retain
  the existing GenericJson diagnostic precedence; validation cannot silently broaden grammar.

## Existing evidence and actual test gap

`JsonParsingCharacterizationTests` fixes UTF-8/Unicode/syntax precedence and lexical spans.
`JsonEditingTests`, `JsonExactValueTests`, `JsonRobustnessBoundaryTests` fix byte-exact scalar
replacement, duplicates, protocol fallback and size/depth/value limits. `JsonArrayEditingTests`
and `JsonObjectEditingTests` fix structural byte output, identity retirement, mixed history,
rename trivia and undo/redo. `JsonTokenValidationTests` fixes streaming hierarchy/value checks.
Existing reference-action, semantic, query, decoder and conformance suites remain required.

The targeted uncovered migration case combines escaped duplicate names, resource-fallback
numbers, an inserted scalar with an unrelated source offset, scalar edits after insertion,
then structural undo back to the original-source patch path. Add a byte-exact golden at every
history transition; do not duplicate the existing independent parser/number boundary tests.

## Sources and validation boundaries

Contract evidence is the baseline plugin implementation and named committed tests above.
Format references already attributed in [README](README.md#sources-and-attribution):
[RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) and
[Utf8JsonReader](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader).
Those references describe syntax; the existing tests define the actual accepted subset.
No private sample values are included. All new cases use original synthetic bytes.
Scratch/build/cache/results belong under
`D:/Temp/saveeditor/test-scratch/sdk-full-roadmap-20261029/genericjson-sdk06`.
Shared SDK work must land on main before merging origin/main into this branch; no framework
patch cherry-picks or hidden integration. Parent owns SDK review, CI and merge.

## Baseline validation result

Release GenericJson suite with the added `JsonSubstrateHistoryTests` golden: **456 total,
423 passed, 33 skipped, 0 failed**. The sample-dependent skips correctly report unavailable
external archives; synthetic mutation fuzzing runs and passes. The new golden passes with
exact bytes at every scalar/insert/remove/undo/redo transition and verifies restored IDs.
Build intermediates, binaries, NuGet cache, CLI home and results were redirected to the scratch
root above using `--artifacts-path`, `--results-directory` and task-local environment settings.

Adoption remains pending the reviewed SDK-06 contract landing on origin/main. No public API
was guessed, no framework code copied, and no structural capabilities removed in preparation.
