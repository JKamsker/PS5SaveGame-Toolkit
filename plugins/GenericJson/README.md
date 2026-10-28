# Generic JSON (`format.json`)

Content-detected UTF-8 JSON primitive editor. **Experimental**: real-sample conformance passes with edits and reopen validation; no console load has been tested. It makes no game-specific semantic claims.

## Coverage and file roles

- PPSA04027 / Cyberpunk 2077: `settings.json` (configuration, 45,415 bytes, version 136) and `metadata.9.json` (slot metadata, 2610 bytes). Binary sav.dat/user.gls are separate formats.
- PPSA07696 / Ridge Racer 2 PSP classic: `imgui_emu_option.json` (emulator configuration, 1320 bytes; cubic_A = -0.5). DATA.BIN is a separate legacy gameplay file.
- PPSA02343 / It Takes Two (EU): `ue4savegame.dpx.sav` is plain JSON with nested string-valued Profile, CloudSettings and LocalSettings. Three primary generations (3,993 / 3,522 / 1,312 bytes) have identical backup twins.
- Other valid UTF-8 JSON files, regardless of filename/title. `Info.Games` is intentionally empty for a format-generic plugin.

Probe returns Format (20) only after full grammar/UTF-8 validation. A 64-byte prefix rejects obvious binary cheaply; plausible text is fully parsed, capped at 16 MiB, 250,000 value nodes and depth 64. UTF-8 BOM is optional and preserved. Comments, trailing commas, trailing non-whitespace, invalid UTF-8 and malformed Unicode strings are rejected with SaveFormatException. Root scalars and null are supported; UTF-16 JSON is rejected.

## Editing and preservation

The parser records each value token's original byte offset/length and ordered tree position. Objects/arrays expand lazily; every expansion contains at most 128 nodes, including hierarchical ranges for very wide containers. Overview statistics are bounded; no large eager UI tree is built.

Raw scalars use F10 `NullableSpec(UnionSpec)` with explicit string, boolean and exact-number choices. A null field is present and editable; a missing property has no field; empty text stays a string. Switching type is an advanced syntax edit with no game-safety claim. Multiline strings are supported and invalid Unicode surrogate input is rejected.

Every editable JSON number uses `FieldValue.ExactNumber`, including small integers, negative zero, long decimals and extreme exponents. No Int128/double/JavaScript Number conversion is involved. Limits are the SDK hard bounds: 16,384 lexeme characters and absolute exponent 1,000,000,000; larger valid tokens remain verbatim read-only. Exponents are validated without expanding zeros. Wire values are explicitly tagged, keeping numeric-looking strings distinct.

Serialize copies original byte spans and replaces **only changed value tokens**. Whitespace, BOM, key order, duplicate keys, untouched escapes and numeric spellings survive exactly. Reverting a value restores its original spelling. Changed strings are JSON-escaped. Invalid union tags/type payloads and invalid surrogate edits reject the whole batch. No static mutable session state or plugin filesystem/network I/O is used.

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

F10 exact-number and nullable/union support is now consumed directly. F5, F6 and F8 are available for the next phases. No new framework request is needed for scalar editing. Browser controls are framework-owned; plugin tests exercise the actual host wire coercion and session undo/redo without modifying shared UI code.

## Sources and attribution

| Dataset / implementation | Source and attribution | Use |
| --- | --- | --- |
| JSON grammar / token reader | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), IETF; [Utf8JsonReader](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader), Microsoft | Standards/API references; no third-party parser code copied. |
| Title and wrapper research | [PROSPEROPatches PPSA02343](https://prosperopatches.com/PPSA02343); [andshrew/PlayStation-PS1HD](https://github.com/andshrew/PlayStation-PS1HD) (MIT) | Research only; no unlicensed catalogue content or PS1 presets embedded. |
| Overlay labels / schemas | Owner's imported samples; [provenance sidecar](SaveToolkit.Plugins.GenericJson/JsonOverlay.source.md) | Names are rendered directly from each file. Original matcher/prose; no distributed game dictionary or save payload. UI shows this provenance. |

### Roadmap evidence baseline

Phase 1 adds read-only Cyberpunk settings v136, It Takes Two gameplay-key summaries and Ridge Racer wrapper groups beside the generic JSON tree. Unknown titles/roles/versions retain generic fallback. These interpretations are inferred, not console-verified. Generic detection still claims no titles in `Info.Games`.

The sample-backed suite currently passes **30 tests, 0 failed, 0 skipped** with `SAVETOOLKIT_ARTIFACTS` set. It uses F1 `JsonSaveDecoder` / `SemanticDiffer` on the three It Takes Two primary generations, restricts comparison to minigame/chapter/checkpoint paths and logs only aggregate counts. All six primary/backup occurrences round-trip byte identically. Archive order is not chronology and cannot establish causal progression or write dependencies.

### Phase 2 exact-value validation

**44 tests passed, 0 failed, 0 skipped** with the private sample path exported in the test command. Added SDK tagged-wire and actual Core session tests for exact lexemes, scalar type choices, null/empty/missing distinction, invalid batches, resource fallbacks and spelling restoration through undo/redo. Browser automation has not been performed in this plugin round.
