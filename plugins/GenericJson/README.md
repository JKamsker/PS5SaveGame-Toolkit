# Generic JSON (`format.json`)

An exact UTF-8 JSON workbench with read-only, sample-backed title overlays. **Experimental**: sample conformance, host sessions and browser round-trips are tested; console acceptance is untested. Raw syntax edits make no game-safety claim. `Info.Games` remains empty because detection is format-based.

## Coverage and evidence

| Title / role | Observed coverage | Interpretation |
| --- | --- | --- |
| PPSA04027 / Cyberpunk 2077 | `settings.json`, 45,415 bytes, version 136; `metadata.9.json`, 2,610 bytes | Read-only file-supplied settings groups/options. Binary `sav.dat` / `user.gls` are separate formats. Metadata uses the raw workbench. |
| PPSA07696 / Ridge Racer 2 PSP classic | `imgui_emu_option.json`, 1,320 bytes; `cubic_A = -0.5` | Read-only wrapper configuration groups. `DATA.BIN` gameplay is separate. PS1 preset documentation does not prove PSP enum meanings. |
| PPSA02343 / It Takes Two (EU) | Three primary `ue4savegame.dpx.sav` generations, 3,993 / 3,522 / 1,312 bytes, plus byte-identical backup twins | Nested string-valued `Profile`, `CloudSettings`, `LocalSettings`. Read-only observed minigame and chapter/checkpoint keys; absent keys stay absent. |
| Other titles / filenames | Complete valid UTF-8 JSON, including root scalars/null | Generic workbench only. A title ID or `.sav` extension does not establish JSON or Unreal GVAS. |

Byte-level shapes, lengths and no-op preservation are tested observations. Gameplay meanings and write dependencies remain **Inferred/Unknown**. Matchers require the title, file role and expected shape; Cyberpunk also requires numeric version 136. Ambiguous required keys, incompatible scalar types and unsupported versions fall back to generic JSON. Overlays are rematched after edits and undo.

F1 `JsonSaveDecoder` / `SemanticDiffer` tests compare the three It Takes Two primary generations, restricting output to aggregate changes in minigame/chapter/checkpoint path families. All six primary/backup occurrences round-trip identically. Archive order is not chronology: these snapshots do not prove a transition, counter unit, flag meaning or dependency. No identity values or private save payloads are committed/logged.

## Exact values and preservation

All editable scalars use F10 `NullableSpec(UnionSpec)` with explicit exact-number, string and boolean choices. A present null, an absent property and an empty string remain distinct. Multiline strings are supported. Unknown fields remain available through the raw workbench with `Confidence.Unknown` and a game-dependency warning.

Numbers retain the exact lexeme, including small integers, negative zero, exponent spelling, long decimals and values beyond Int128/double. There is no conversion through JavaScript `Number`. Tagged wire values distinguish numbers from numeric-looking strings. SDK bounds are 16,384 lexeme characters and absolute exponent 1,000,000,000; larger valid tokens remain verbatim read-only. These are resource bounds, not game-specific numeric limits.

The ordered token model retains original source slices, object-member occurrences, container punctuation/trivia and immutable session identities. Unchanged scalar tokens, key spelling, escapes, key order, BOM and unaffected whitespace survive exactly. Changed strings are JSON-escaped; changing a value back restores its imported spelling. Array punctuation follows the surviving entry's separator, or the container's existing separator/indent style for a new entry. Undo restores the entire original byte sequence, not merely equivalent JSON.

Every changed output is reparsed and checked against the live token tree. Invalid Unicode, malformed tagged values, depth/node/size overflow and failed round-trips reject atomically before export. Only the opened file is emitted. JSON has no internal checksum/encryption layer; external game integrity and companion-file dependencies are unknown. Plugin code performs no filesystem/network I/O and retains no static mutable session state.

## Search, review and reset

The Search tab uses F8 to query the complete live tree independently of expanded sections. Rows have stable identities, occurrence-aware paths, JSON types and scalar edit details. Duplicate object members are shown separately. Free text matches keys, displayed paths and full live scalar values. Type filtering is exact; other column filters use case-insensitive substring matching. Sorting is lexical with stable identity tie-breaking. Results refresh after edits; editing a match uses the normal field controls in row details.

F6 supplies the original/current Changes panel and individual scalar reverts. Read-only child-sequence fields expose array membership/order changes, including changes beyond the displayed identity prefix. Added null differs from a missing node. The original baseline survives view refresh and undo-history eviction. Review coverage is bounded by Core; an incomplete warning must not be treated as a complete file diff. Use undo for structural restoration.

“Reset existing values to imported values” uses preview/apply, either for the document or selected search rows, including F8 “select all matching.” One apply is one undo step. It restores original values/spellings, **not game defaults**. New nodes, containers and read-only values are skipped; removed nodes and ordering require undo. No absent game keys or guessed defaults are created.

The current shared browser review formatter renders F10 tagged values as `[object Object]`. Typed API values, review identity and revert behavior are correct. Action labels include exact JSON literal transitions as a mitigation; the JSON editor/download remains authoritative until the shared formatter is fixed. See **SDK requests** below.

## Reversible array editing

Every array offers previewed insert, remove, move and duplicate actions using F5 `BoundSaveDocument.Structural`. Insertion requires an explicit JSON template (default `null`); duplication copies the selected subtree's current values. Move indices are within the same array, and the destination is the final index. These are advanced syntax operations with an explicit game-dependency warning.

Planning clones the tree and validates the prospective serialization without changing the document. Applying checks the current preview again and produces a document-owned snapshot. Core handles revision checks, history and stale binding/action/lazy-cache retirement. Existing node IDs survive changes before them; removed IDs return on undo; newly allocated IDs are never recycled on an undo branch. Mixed value/structure undo and redo preserve exact bytes and identities.

Object member add/remove/rename, cross-array moves, schema-specific templates and game presets are not implemented. No schema-derived safe insertion is implied.

## Resource bounds

| Operation | Bound / behavior |
| --- | --- |
| Parse / export | 16 MiB UTF-8, 250,000 value nodes, depth 64; optional preserved UTF-8 BOM. Comments, trailing commas, extra roots, malformed Unicode/UTF-8 and UTF-16 reject. |
| Lazy navigation | At most 128 child nodes per expansion; wide containers use hierarchical ranges. |
| Search | At most 200 rows per request; unsorted queries retain only the requested page. Sorting requires at most 10,000 matching rows. Displayed paths are clipped with an identity suffix; full keys/scalar values remain searchable. |
| Array insert | At most 1 MiB of template characters, plus normal parse/export bounds. |
| Action review / reset | At most 5,000 affected review fields; host plan-size and query-selection bounds also apply. |
| Structural source retention | At most 32 MiB of distinct source buffers in the live document; export/reopen can release retained source history. Core separately bounds undo snapshots. |
| Changes panel | Core traversal/resource limits apply (currently 100,000 visited view nodes and depth 128, plus its configured baseline byte budget). Large/deep review may be incomplete even though full-tree search still works. |

## Validation

Run sample-backed tests with the environment variable in the **same shell command**:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Validated 2026-10-28: **83 passed, 0 failed, 0 skipped**; API build **0 warnings, 0 errors**. Tests cover sample conformance/backup twins, content detection, conservative overlay fallback, exact tagged host wire values, null/missing/empty/type changes, atomic invalid-input/resource rejection, collapsed 40,001-node search, live filters, baseline preservation, selected resets, structural boundaries, nested duplication, duplicate keys, stale requests, identity retirement and mixed undo/redo. Tests skip when private samples are absent; a skipped run does not count as sample validation.

The existing shared frontend suites for scalar controls, values, review and query selection also passed: **51 tests in 4 files**. TypeScript/Vite production build passed. No shared UI files were changed.

Browser validation used only a synthetic BOM-prefixed document containing an exact exponent number, null, empty text and an array. The normal controls edited a long tiny exact number, switched null to empty text, previewed/applied an array insertion containing a huge exact number, and exported exact lexemes. Two undo steps after the numeric edit/insertion restored the original bytes including BOM, with zero remaining changes. The review formatter limitation was reproduced and reported in #18. No console load/restart/resave has been performed.

## Roadmap status and evidence gates

| Plan phase | This round |
| --- | --- |
| 1: read-only interpretation | Conservative title/role/shape matching, bounded file-derived summaries, provenance, It Takes Two F1 comparison and sample/twin coverage shipped. Complete friendly-name/schema catalogues remain open. |
| 2: exact JSON workbench | F10 exact lexemes, nullable/union controls, authoritative validation, wire/session/browser checks shipped. |
| 3: existing-value editing and review | Generic F8 search, F6 review/revert and imported-value reset shipped. Title-specific coupled enum/default/flag/progression edits remain disabled pending evidence. |
| 4: reversible structure | Generic previewed array insert/remove/move/duplicate and mixed history shipped. Optional object operations and schema-aware templates remain open. |
| 5: presets / complete reference packs | Deferred: no proven dependency matrix, complete checkpoint corpus or console validation. |

Owner capture checklist before semantic writes or compatibility claims:

1. Record title/region, game build, wrapper/settings version, exact file roles and action for every pair. Export immediately before and after **one** change; retain all companions and duplicate/backup occurrences privately. Record unchanged control captures to identify churn.
2. Cyberpunk: capture one static enum change; one scalar change and reset-to-default; one dynamic language/HDR choice. Restart, resave and re-export each. Include a second settings version to test schema fallback and determine `value`/`index` authority.
3. It Takes Two: capture a fresh profile; discover one minigame; play it; separate Cody and May wins/scores; enter a chapter; cross one checkpoint. Capture incomplete and complete profiles on the same version, preferably from two owners. Record Continue/Chapter Select/replay behavior; do not use archive order as chronology.
4. Ridge Racer: capture before/after each filter/upscale/AA preset and one controller/rewind option, recording the PSP wrapper build. Restore, restart and re-export to establish numeric enum domains.
5. On spare saves, validate each proposed edit's exact menu/game effect, restart/resave/re-export, then compare unrelated state and all companions. Only the owner promotes maturity to Stable. No trophy, cross-platform conversion or arbitrary raw edit compatibility is promised.

## Sources and attribution

| Dataset / implementation | Source / author / rights | Use |
| --- | --- | --- |
| JSON grammar | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), IETF | Standard reference; no implementation copied. |
| Token parser | [Utf8JsonReader](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader), Microsoft / .NET runtime | Runtime API; original token/trivia model and writer. No community parser code copied or executed. |
| Title identification | [PROSPEROPatches PPSA02343](https://prosperopatches.com/PPSA02343), PROSPEROPatches; no reuse licence established | Research only. No catalogue text, artwork or dictionary embedded. |
| Wrapper research | [PlayStation-PS1HD](https://github.com/andshrew/PlayStation-PS1HD), andshrew, MIT | Research only; no code, preset table or prose adapted. PS1 meanings are not applied to PSP values. |
| Overlay names / shapes | Owner-supplied private samples; [provenance sidecar](SaveToolkit.Plugins.GenericJson/JsonOverlay.source.md) | Labels come directly from each imported file. Original matcher/prose; no distributed save payload or game dictionary. UI shows provenance. |
| Earlier byte reconnaissance | [Cyberpunk](../../docs/formats/PPSA04027.md), [Ridge Racer](../../docs/formats/PPSA07696.md), [roadmap research](../../docs/plans/GenericJson.md) | Context and additional primary research links; not authority for untested writes. |

No third-party reference dataset is bundled, so there is no adapted CC BY-SA/MIT data notice to carry beyond the source records above. References are also exposed through `Info.References`.

## SDK requests

F10, F5, F8 and F6 are consumed directly; no plugin-local substitute history, query or diff protocol was added. One shared integration fix remains:

- `web/src/api/actionReview.ts` currently formats non-null values with `String(value)`. Format `ExactNumber` and nested `Union` values recursively, preserving the numeric lexeme; quote strings so numeric-looking strings, numbers, null and absence remain distinct in both Changes and action previews. Add a combined F10/F6 browser regression test. This was reported in [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18#issuecomment-6479348148). Plugin mitigation is exact JSON transitions in action labels and a visible Overview warning; shared UI changes are outside this worker's scope.
