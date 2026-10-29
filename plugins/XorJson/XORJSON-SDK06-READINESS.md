# XorJson SDK-06 readiness

## Baseline characterization

XorJson decodes a complete file through `JsonEncoding`: plain character JSON is
accepted only for the observed `FCharacterSaveV1` envelope, while settings and
generic documents use the byte-minus-one or synthetic XOR-1 transform. The
transform, title/version/offline gate, integrity warning, file roles and group
transaction policy are plugin-owned and must remain so.

The decoded UTF-8 parser currently lives in `JsonTokenTree`. It accepts one JSON
value, an optional UTF-8 BOM, and JSON whitespace; it rejects malformed UTF-8,
duplicate-free assumptions, comments and trailing commas. It bounds input to
16 MiB, depth to 64 and values to 250,000. Each value retains its source byte
array, UTF-8 offset and length. Object members retain their decoded name,
source key/colon span and a per-name occurrence number, so duplicate properties
remain distinct. Scalars retain their exact original lexeme and typed
`FieldValue`; oversized valid number lexemes remain verbatim and read-only.

`JsonTokenWriter` preserves the BOM, trivia, key spelling, punctuation and all
untouched source spans. It replaces only changed scalar spans and reparses every
output. Structural add/remove/replace is performed by `JsonEditModel`; detached
snapshots preserve the plugin's allocated IDs and undo/redo history. The writer
and model intentionally keep structural identity and envelope handling outside
the shared SDK substrate.

## Minimum SDK-06 contract

The plugin can adopt the additive contract
`SaveToolkit.Sdk.Json.LosslessJsonDocument.Parse(ReadOnlySpan<byte>,
LosslessJsonLimits)` when it provides:

* a lossless node tree with stable preorder IDs, parent/children, decoded member
  names, raw property-name lexemes, duplicate occurrence numbers and original
  byte spans;
* original and current scalar lexemes independently of `ExactJsonNumber`, so a
  valid but oversized number can be displayed and preserved read-only;
* scalar-only replacement with the existing exact-number and Unicode/type
  validation semantics;
* serialization that is byte-identical for a no-op and retains BOM/trivia and
  untouched bytes after a scalar patch;
* detached snapshots that can be captured/restored for scalar history;
* the existing 16 MiB, depth-64 and 250,000 lexical-token/value safety limits,
  with deterministic malformed-input diagnostics.

XorJson will map SDK nodes to its existing `value/{Index}` bindings and preserve
its typed-path and duplicate-member identity strings. Structural operations,
`JsonEditModel` snapshots, XOR/shift transforms, character/settings gates,
group roles and multi-file transaction/undo policy remain local. The plugin
must not replace functioning structural editing with a scalar-only SDK API.

## Golden coverage required before adoption

The existing tests already cover both transforms, BOM and trivia, duplicate
members, escaped/unicode strings, exact number spelling and null/empty values,
scalar length changes, malformed and limit inputs, structural history,
multi-file group snapshots and no-op encoded bytes. The migration must retain
those assertions through the SDK adapter and add a focused SDK parity fixture
that compares decoded bytes and complete transformed envelope bytes before and
after scalar edits, including an oversized number that remains read-only.

The sample-dependent suite continues to skip when private samples are absent;
synthetic fixtures are the source of migration evidence. A clean-session export
must be compared through complete `FileOutput.Content` bytes rather than an
empty delta map.

## Known boundary

SDK-06 does not establish game semantics, external integrity, console
acceptance, structural identities or transformed envelopes. Those remain
explicitly `Unknown` or `Inferred` in the plugin README and require owner
validation on a disposable save slot.
