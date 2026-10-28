# Generic JSON (`format.json`)

Content-detected UTF-8 JSON primitive editor. **Experimental**: real-sample conformance passes with edits and reopen validation; no console load has been tested. It makes no game-specific semantic claims.

## Coverage and file roles

- PPSA04027 / Cyberpunk 2077: `settings.json` (configuration, 45,415 bytes, version 136) and `metadata.9.json` (slot metadata, 2610 bytes). Binary sav.dat/user.gls are separate formats.
- PPSA07696 / Ridge Racer 2 PSP classic: `imgui_emu_option.json` (emulator configuration, 1320 bytes; cubic_A = -0.5). DATA.BIN is a separate legacy gameplay file.
- Other valid UTF-8 JSON files, regardless of filename/title. `Info.Games` is intentionally empty for a format-generic plugin.

Probe returns Format (20) only after full grammar/UTF-8 validation. A 64-byte prefix rejects obvious binary cheaply; plausible text is fully parsed, capped at 16 MiB, 250,000 value nodes and depth 64. UTF-8 BOM is optional and preserved. Comments, trailing commas, trailing non-whitespace, invalid UTF-8 and malformed Unicode strings are rejected with SaveFormatException. Root scalars and null are supported; UTF-16 JSON is rejected.

## Editing and preservation

The parser records each value token's original byte offset/length and ordered tree position. Objects/arrays expand lazily; every expansion contains at most 128 nodes, including hierarchical ranges for very wide containers. Overview statistics are bounded; no large eager UI tree is built.

Strings use TextSpec, booleans BoolSpec. Number tokens without decimal point/exponent use Int128 IntegerSpec; decimal/exponent tokens use FloatSpec when representable as finite double. Integers outside Int128 and non-finite/underflowing numeric conversions remain verbatim read-only text, with a warning. Null is displayed read-only. Duplicate property names remain separate occurrences, with distinct stable value IDs based on tree order rather than byte offsets or ambiguous property paths.

Serialize copies original byte spans and replaces **only changed value tokens**. Whitespace, BOM, key order, duplicate keys, untouched escapes and numeric spellings survive exactly. Reverting a value restores the original token spelling. Changed strings are JSON-escaped; changed floating tokens keep a decimal/exponent marker so reopening retains FloatSpec. Double precision can round original decimals, so float edits carry an explicit precision warning; untouched tokens are never rounded. Type changes, NaN/infinity and invalid surrogate edits are rejected atomically. No static mutable session state or plugin filesystem/network I/O is used.

Output is parsed again inside Serialize and changed values must match the new parse. No-op serialization is byte-identical. Only the opened file is returned; dependencies in other JSON/binary files are not inferred. Game-specific relationships, identifiers and allowed ranges remain **Confidence.Unknown** with a field warning. No item names, gameplay meanings or safe presets are invented.

JSON itself has no checksum/encryption/compression layer. External game integrity/dependency checks are unknown; syntax-valid editing does not certify a game's acceptance. Embedded binary/encrypted/compressed formats are not claimed as JSON.

## Validation

```powershell
dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests
pwsh -NoProfile -File plugins/GenericJson/scripts/check-source-lines.ps1
```

24 tests cover real-sample conformance with meaningful primitive edits, every supplied JSON snapshot/backup no-op, known settings values, formatting/token preservation, undo, stable IDs after length changes, duplicate keys, root scalars/null, Unicode, numeric limits, atomic rejection and bounded large trees. Missing sample tests skip through SampleSaves; synthetic tests still run. The plugin depends only on the SDK and the .NET runtime, with no added packages.

The line-count script warns above 300 and rejects above 500 physical C# lines; `githooks/pre-commit` calls it. The repository owner can chain this scoped hook into shared hooks/CI. This worker did not replace shared hook settings or add an out-of-scope root pipeline (none exists in this worktree).

## Sources and licensing

- [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259): JSON grammar, scalars, Unicode and numeric interoperability limits.
- [Utf8JsonReader documentation](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader): runtime token reader, TokenStartIndex/BytesConsumed and validation API. No third-party parser code was copied or executed.
- [Cyberpunk byte reconnaissance](../../docs/formats/PPSA04027.md), [Ridge Racer byte reconnaissance](../../docs/formats/PPSA07696.md): actual sample identities/roles and known option values. See those reports for primary game-format research.

## SDK requests

No framework changes are required. Optional future specs for editable null/nullable primitives and arbitrary-precision JSON numbers would avoid read-only fallbacks. The current SDK's Int128/double range cannot represent every valid JSON number without loss. A game-specific schema plugin remains the appropriate place for coupled-file edits and integrity rules.
