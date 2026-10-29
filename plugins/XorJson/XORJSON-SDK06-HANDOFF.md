# XorJson SDK-06 handoff

## Adoption result

XorJson now uses the shared `SaveToolkit.Sdk.Json.LosslessJsonDocument` for the
decoded JSON lexical tree and unchanged-structure scalar patching. `JsonTokenTree`
maps SDK preorder nodes into the existing plugin `JsonValueToken` model, retaining
the original `value/{Index}` IDs, typed paths, duplicate occurrences, raw quoted
property spans, exact scalar values and read-only oversized-number fallback.
`JsonTokenWriter` delegates scalar replacement and serialization to the SDK when
the local child order and source identity are unchanged. This preserves BOM,
trivia, key spelling, escapes, unknown values and untouched spans without a
second plugin lexer or scalar writer.

The plugin still owns structural add/remove/replace, allocation and identity
rules, detached structural mementos, F6 action planning, F7 group transactions,
title/version/offline gates, integrity warnings and XOR-1/byte-minus-one/plain
envelope transforms. Structural changes use the existing local writer and never
attempt to mutate the SDK's read-only container tree. SDK document snapshots are
captured as copied replacement dictionaries; restored plugin tokens are rebound
by stable parse ID and source span to the current document's owned SDK nodes.
Scalar history applies those value-owned lexemes through the current document,
so a fresh group candidate cannot alias the original document or node state.

## R1 response matrix

- **P1 scalar batch budget:** `JsonTokenWriter` classifies every final scalar
  lexeme and applies shrinking/reverting replacements before growth. The SDK
  document is snapshotted first and restored on every supported write/parse
  failure, then final bytes are reparsed. A valid final-fitting batch is now
  independent of source order while retaining atomicity.
- **P1 fresh group ownership:** `JsonModelSnapshot` contains cloned token
  values, cloned source buffers and a copied scalar replacement map. Restore
  binds the clone to the current `LosslessJsonDocument` by stable ID and exact
  source span; no SDK document or node reference crosses a memento boundary.
  Fresh-group tests mutate a restored candidate, exercise a rejected mixed write,
  then restore mixed structural history while asserting the original group's
  encoded bytes remain unchanged.

## Dependency and branch

- SDK dependency PR: [#51](https://github.com/JKamsker/PS5SaveGame-Toolkit/pull/51)
- Reviewed dependency head: `72490fe8756627013c73c0b80fa874b563a0e652`
- Dependency merge in this branch: `49a6b80b` (ordinary merge; no cherry-pick or
  copied SDK source)
- Assigned branch: `t3/xorjson-sdk06-adoption`
- Branch base before dependency merge: `e76e51035fec308a56e5c26b5d97a9c08089f881`
- Consumer head before R1 fixes: `8d7c4c17691b1de27e37922bfda3d7cb67177645`
- Consumer follow-up head: `ab568661cc1a2945ad209c1091e98aa5fcfb41e9` (`fix(xorjson): close SDK06 scalar and snapshot gaps`)
- Consumer PR base: `t3/sdk06-lossless-json` / reviewed SDK head above; parent may
  retarget the PR to `main` after #51 lands.

The consumer diff authored by this branch is confined to `plugins/XorJson/`.
The framework files visible in the checkout come only from the explicit ordinary
dependency merge and are not part of the consumer changeset against PR #51.

## Preservation evidence

The full plugin suite exercises the shared path for no-op exports, BOM and
trivia, duplicate member occurrences, escaped and Unicode strings, exact number
lexemes, null versus empty text, scalar length changes, oversized read-only
numbers, malformed UTF-8/JSON and resource limits. Structural tests continue to
cover add/remove/replace, stable IDs, local trivia/key preservation and mixed
scalar/structural undo/redo. Group tests cover multi-file snapshots and exact
encoded outputs for both transforms. New synthetic tests verify two parsed
documents do not share scalar history, exact max-byte growth-before-shrink
batches in both edit orders with BOM/trivia and multibyte source text, and
fresh two-document and group candidate isolation through failed writes, undo /
redo, and mixed structural restore. A changed first document retains
the oversized number, duplicate members and source bytes of the second document.

Validation from the sample-free run:

`dotnet run --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests/SaveToolkit.Plugins.XorJson.Tests.csproj --no-build -- -noLogo -noColor`

passed **301 tests**, with **19 sample-dependent skips** and **0 failures**.
The plugin project also builds with zero warnings. Missing `PPSA16064` samples
were skipped by the existing test harness; no private sample bytes were copied.
PR CI metadata: checks are pending on the dependency-base PR; parent owns CI
watch and will revalidate after SDK PR #51 lands on `main`.

## Remaining limits

SDK-06 is scalar-only. JSON structural edits remain local and retain the prior
Experimental confidence, title gates and console/integrity warnings. The SDK
does not establish game semantics, external integrity, console acceptance or
safe structural dependencies. The plugin continues to preserve all unknown data
and reports unsupported/oversized scalar values as read-only.
