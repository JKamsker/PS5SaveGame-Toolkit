# Generic JSON (`format.json`)

An exact UTF-8 JSON workbench with read-only, sample-backed title overlays. **Experimental**: sample conformance, host sessions and browser round-trips are tested; console acceptance is untested. Raw syntax edits make no game-safety claim. `Info.Games` remains empty because detection is format-based.

## Coverage and evidence

| Title / role | Observed coverage | Interpretation |
| --- | --- | --- |
| PPSA04027 / Cyberpunk 2077 | `settings.json`, 45,415 bytes, version 136; 28 groups / 149 options; `metadata.9.json`, 2,610 bytes | Read-only friendly group/option labels, recorded types/bounds/defaults/choices and exact index/value consistency. Binary `sav.dat` / `user.gls` are separate formats. Metadata uses the raw workbench. |
| PPSA07696 / Ridge Racer 2 PSP classic | `imgui_emu_option.json`, 1,320 bytes; `cubic_A = -0.5` | Read-only wrapper configuration groups. `DATA.BIN` gameplay is separate. PS1 preset documentation does not prove PSP enum meanings. |
| PPSA02343 / It Takes Two (EU) | Three primary `ue4savegame.dpx.sav` generations, 3,993 / 3,522 / 1,312 bytes, plus byte-identical backup twins | String-valued `Profile` alongside `CloudSettings` / `LocalSettings`. Read-only grouped `Minigame.*`, `MinigameFlag.*`, `MinigameCounter.*` and chapter/checkpoint keys; absent keys stay absent. |
| Other titles / filenames | Complete valid UTF-8 JSON, including root scalars/null | Generic workbench only. A title ID or `.sav` extension does not establish JSON or Unreal GVAS. |

Byte-level shapes, lengths and no-op preservation are tested observations. Gameplay meanings and write dependencies remain **Inferred/Unknown**. Matchers require the title, file role and expected shape; Cyberpunk also requires numeric version 136. Ambiguous required keys, incompatible scalar types and unsupported versions fall back to generic JSON. Overlays are rematched after edits and undo.

F1 `JsonSaveDecoder` / `SemanticDiffer` tests compare the three It Takes Two primary generations, restricting output to aggregate changes in minigame/chapter/checkpoint path families. All six primary/backup occurrences round-trip identically. Archive order is not chronology: these snapshots do not prove a transition, counter unit, flag meaning or dependency. No identity values or private save payloads are committed/logged.

Friendly labels use original word splitting of keys already in the live document, not an external localization/alias dictionary. Raw names, occurrence paths and quoted JSON string values remain visible with provenance. Cyberpunk metadata is descriptive: a matching `values[index]` does not prove which paired writes the game accepts. Dynamic choices without recorded lists remain unknown. It Takes Two flag casing (`true` / `True`) and score strings are displayed without normalization; no identity, EULA or telemetry fields are added to its gameplay overlay. Complete minigame aliases and chapter/checkpoint graphs remain owner-gated.

## Exact values and preservation

All editable scalars use F10 `NullableSpec(UnionSpec)` with explicit exact-number, string and boolean choices. A present null, an absent property and an empty string remain distinct. Multiline strings are supported. Unknown fields remain available through the raw workbench with `Confidence.Unknown` and a game-dependency warning.

Numbers retain the exact lexeme, including small integers, negative zero, exponent spelling, long decimals and values beyond Int128/double. There is no conversion through JavaScript `Number`. Tagged wire values distinguish numbers from numeric-looking strings. SDK bounds are 16,384 lexeme characters and absolute exponent 1,000,000,000; larger valid tokens remain verbatim read-only. These are resource bounds, not game-specific numeric limits.

The ordered token model retains original source slices, object-member occurrences, container punctuation/trivia and immutable session identities. Unchanged scalar tokens, key spelling, escapes, key order, BOM and unaffected whitespace survive exactly. Changed strings are JSON-escaped; changing a value back restores its imported spelling. Array punctuation follows the surviving entry's separator, or the container's existing separator/indent style for a new entry. Undo restores the entire original byte sequence, not merely equivalent JSON.

Every changed output is reparsed and checked against the live token tree. Invalid Unicode, malformed tagged values, depth/node/size overflow and failed round-trips reject atomically before export. Only the opened file is emitted. JSON has no internal checksum/encryption layer; external game integrity and companion-file dependencies are unknown. Plugin code performs no filesystem/network I/O and retains no static mutable session state.

## Search, review and reset

The Search tab uses F8 to query the complete live tree independently of expanded sections. Rows have stable identities, occurrence-aware paths, JSON types, imported JSON literals and scalar edit details. Duplicate object members are shown separately. Free text matches raw keys, word-split friendly labels, displayed paths, exact `value/N` identities and full live scalar values. JSON type and comparison filters are exact; other text filters use case-insensitive substring matching. Sorting is lexical with stable identity tie-breaking. Results refresh after edits; editing a match uses the normal field controls in row details.

Comparison states are `unchanged`, `modified` and `added`. They describe direct scalar/type/name changes or a container's direct membership/order/name signature; descendant edits do not automatically mark every ancestor modified. Imported strings are quoted and exact numbers retain their spelling. Added null shows `(absent)` as its imported value. Removed entries appear in F6 Changes, not this live tree. Container matches open a bounded lazy subtree with normal controls and occurrence-path breadcrumbs. Filter **Subtree identity** by one exact `value/N` to include that node and all live descendants; it survives rename/reorder and becomes empty when the node is removed. Other node IDs sharing a text prefix are not included.

F6 supplies the original/current Changes panel and individual scalar reverts. Read-only child-sequence fields expose membership/order and exact object-name changes; their digests cover entries beyond the bounded displayed prefix. Added null differs from a missing node. The original baseline survives view refresh and undo-history eviction. Review coverage is bounded by Core; an incomplete warning must not be treated as a complete file diff. Use undo for structural restoration.

“Reset existing values to imported values” uses preview/apply, either for the document or selected search rows, including F8 “select all matching.” One apply is one undo step. It restores original values/spellings, **not game defaults**. New nodes, containers and read-only values are skipped; removed nodes and ordering require undo. No absent game keys or guessed defaults are created.

The shared tagged-value review formatter was fixed in `03b66b7`. Exact numbers, strings, null and absence now render distinctly in preview/Changes. Explicit JSON transitions remain in action labels for context; the obsolete host-limitation warning has been removed.

## Reversible structure and templates

Every array offers previewed insert, remove, move and duplicate actions using F5 `BoundSaveDocument.Structural`. Insertion requires an explicit JSON template (default `null`); duplication copies the selected subtree's current values. Move indices are within the same array, and the destination is the final index. These are advanced syntax operations with an explicit game-dependency warning.

Objects offer insert/remove/rename, addressed by zero-based occurrence order. Insert and rename require a unique new name; existing duplicate names can be removed or renamed by explicitly selecting their occurrence index. Preview retains occurrence paths. Renaming changes only the key token, preserving its colon/trivia and value bytes; returning to the imported name restores its original escapes. Empty and Unicode names remain valid JSON. No schema-authorized game member insertion is implied.

Planning clones the tree and validates the prospective serialization without changing the document. Applying checks the current preview again and produces a document-owned snapshot. Core handles revision checks, history and stale binding/action/lazy-cache retirement. Existing node IDs survive changes before them; removed IDs return on undo; newly allocated IDs are never recycled on an undo branch. Mixed value/structure undo and redo preserve exact bytes and identities.

Both insertion forms offer explicit null/string/number/boolean/object/array templates. They never copy a neighboring row or fabricate absent gameplay flags. **Custom JSON** preserves the supplied literal. **Generate from explicit local schema** accepts a user-supplied, local-only subset described in [TEMPLATE_SCHEMA.md](TEMPLATE_SCHEMA.md). Custom/type templates can also be validated against the supplied schema. Generation emits required object members and minimum array entries, using explicit defaults or empty values of the selected type; optional members remain absent. Schema defaults are used only in this explicit generation action, not while validating or editing existing saves. Unsupported keywords and external references reject instead of being silently ignored.

Template schemas apply only to the inserted fragment during preview/apply. They are not inferred title schemas, persistent document constraints or evidence of game-safe values. Cross-container moves, complete JSON Schema, built-in title insertion schemas and game presets remain unimplemented.

## Companion files and F7

F7 document groups (#29, merged as `7ff21ad`) were reviewed. No current GenericJson title has proven coordinated JSON writes, so this plugin remains a single-file document and emits only its opened file. Cyberpunk settings, slot metadata and binary gameplay coexist but have no established cross-file write dependency. It Takes Two's `Profile`, `CloudSettings` and `LocalSettings` are sections of one file, not separate companions. Ridge Racer's wrapper options and binary `DATA.BIN` have different roles without a proven coordinated edit. Identical backup twins alone are not evidence for a new group; ordinary export mirroring remains the host's responsibility.

Before F7 adoption, the owner must supply same-title/same-generation controlled pairs retaining every companion, identify the invariant requiring coordinated changes, and demonstrate failure/success of separate/atomic changes on spare saves. Future groups must pin exact file roles and fingerprints, reject ambiguous/missing partners and explicitly include backups if they are owned. See [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md).

## Resource bounds

| Operation | Bound / behavior |
| --- | --- |
| Parse / export | 16 MiB UTF-8, 250,000 value nodes, depth 64; optional preserved UTF-8 BOM. Comments, trailing commas, extra roots, malformed Unicode/UTF-8 and UTF-16 reject. |
| Lazy navigation | At most 128 child nodes per expansion; wide containers use hierarchical ranges. |
| Search | At most 200 rows per request; unsorted queries retain only the requested page. Sorting requires at most 10,000 matching rows. Displayed paths are clipped with an identity suffix; full keys/scalar values remain searchable. |
| Array/object insert | At most 1 MiB of template characters, plus normal parse/export bounds. New object names: at most 16,384 UTF-16 characters. |
| Explicit template schema | 64 Ki UTF-16 characters, JSON depth 16; generation at most 4,096 nodes and 128 generated entries per array (larger explicit defaults still obey total bounds). No network/reference resolution. |
| Action review / reset | At most 5,000 affected review fields; host plan-size and query-selection bounds also apply. |
| Structural source retention | At most 32 MiB of distinct source buffers in the live document; export/reopen can release retained source history. Core separately bounds undo snapshots. |
| Changes panel | Core traversal/resource limits apply (currently 100,000 visited view nodes and depth 128, plus its configured baseline byte budget). Large/deep review may be incomplete even though full-tree search still works. |

## Validation

Run sample-backed tests with the environment variable in the **same shell command**:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Round-2 validation, 2026-10-28: **139 passed, 0 failed, 0 skipped**; API build **0 warnings, 0 errors**. Tests cover sample conformance/backup twins, content detection, conservative overlay fallback, richer read-only overlays across every generation, exact tagged host wire values, null/missing/empty/type changes, atomic invalid-input/resource rejection, collapsed 40,001-node search, imported comparison/subtree filters, lazy container navigation, baseline preservation, selected resets, structural boundaries, object rename/insert/remove, nested duplication, duplicate keys, stale requests, identity retirement, schema validation/generation/budgets and mixed undo/redo. Tests skip when private samples are absent; a skipped run does not count as sample validation.

The existing shared frontend suite passed: **97 tests in 13 files**. TypeScript/Vite production build passed. Dependencies, frontend copy/build output and browser workspaces were isolated under TEMP; no shared source files were changed.

Round-2 browser validation used only synthetic BOM-prefixed JSON. The normal object insertion dialog generated an explicitly supplied object schema containing a long exact exponent number; preview showed its exact typed value and absence correctly. Applying and downloading preserved both original and inserted lexemes and the BOM. Search by container identity opened its lazy subtree and exact-number control. One undo restored byte-identical input, with complete F6 review reporting zero changes. The temporary API and owned browser tab were closed. No console load/restart/resave has been performed.

## Roadmap status and evidence gates

| Plan phase | This round |
| --- | --- |
| 1: read-only interpretation | Title/role/shape matching, richer file-derived option metadata, word-split names, grouped profile flags/counters, provenance, F1 comparison and all sample/twin coverage shipped. Complete localization/alias/checkpoint catalogues remain open. |
| 2: exact JSON workbench | F10 exact lexemes, nullable/union controls, authoritative validation, wire/session/browser checks shipped. |
| 3: existing-value editing and review | F8 search with imported comparison, exact subtree scopes and lazy navigation; F6 review/revert/reset and object-name review shipped. Title-specific coupled enum/default/flag/progression edits remain disabled pending evidence. |
| 4: reversible structure | Previewed array insert/remove/move/duplicate, object insert/remove/rename, explicit local-schema/type templates and mixed history shipped. Built-in title schemas and cross-container moves remain open. |
| 5: presets / complete reference packs | Deferred: no proven dependency matrix, complete checkpoint corpus or console validation. |

Owner capture checklist before semantic writes or compatibility claims:

1. Record title/region, game build, wrapper/settings version, exact file roles and action for every pair. Export immediately before and after **one** change; retain all companions and duplicate/backup occurrences privately. Record unchanged control captures to identify churn.
2. Cyberpunk: capture one static enum change; one scalar change and reset-to-default; one dynamic language/HDR choice. Restart, resave and re-export each. Include a second settings version to test schema fallback and determine `value`/`index` authority.
3. It Takes Two: capture a fresh profile; discover one minigame; play it; separate Cody and May wins/scores; enter a chapter; cross one checkpoint. Capture incomplete and complete profiles on the same version, preferably from two owners. Record Continue/Chapter Select/replay behavior; do not use archive order as chronology.
4. Ridge Racer: capture before/after each filter/upscale/AA preset and one controller/rewind option, recording the PSP wrapper build. Restore, restart and re-export to establish numeric enum domains.
5. On spare saves, validate each proposed edit's exact menu/game effect, restart/resave/re-export, then compare unrelated state and all companions. Only the owner promotes maturity to Stable. No trophy, cross-platform conversion or arbitrary raw edit compatibility is promised.
6. For any candidate companion dependency, retain all files from the same imported generation and backup set. Record the minimal cross-file invariant, run F1 with unchanged controls, and test whether a single-member edit is rejected/overwritten while the coordinated edit survives restart/resave. Co-location alone does not authorize F7 grouping.
7. Supply owner-verified menu labels/localization, complete chapter/checkpoint paths and minigame aliases with exact raw-key mappings. For a built-in title template, identify each required/optional member, legal default, array limit and dependency using controlled pairs; test creation on a fresh/incomplete spare profile. Do not include account IDs, credentials or private payloads in public notes.

The detailed capture checklist and current write gates are maintained in [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md).

## Sources and attribution

| Dataset / implementation | Source / author / rights | Use |
| --- | --- | --- |
| JSON grammar | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), IETF | Standard reference; no implementation copied. |
| Token parser | [Utf8JsonReader](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader), Microsoft / .NET runtime | Runtime API; original token/trivia model and writer. No community parser code copied or executed. |
| Explicit template vocabulary | [JSON Schema reference](https://json-schema.org/understanding-json-schema/), JSON Schema project; specification/reference only | Original bounded subset implementation; no documentation text, example, code or external schemas copied. [Provenance and restrictions](SaveToolkit.Plugins.GenericJson/JsonTemplates.source.md). |
| Title identification | [PROSPEROPatches PPSA02343](https://prosperopatches.com/PPSA02343), PROSPEROPatches; no reuse licence established | Research only. No catalogue text, artwork or dictionary embedded. |
| Wrapper research | [PlayStation-PS1HD](https://github.com/andshrew/PlayStation-PS1HD), andshrew, MIT | Research only; no code, preset table or prose adapted. PS1 meanings are not applied to PSP values. |
| Overlay names / shapes | Owner-supplied private samples; [provenance sidecar](SaveToolkit.Plugins.GenericJson/JsonOverlay.source.md) | Raw keys and metadata come from each imported file; friendly labels use original word splitting. Original matcher/prose; no distributed save payload or game dictionary. UI shows provenance. |
| Cross-platform editor research | [John's It Takes Two editor](https://johnssaveeditors.com/games/it-takes-two/); no reuse licence established | Research only, rechecked 2026-10-28. PC string/profile field names corroborate investigation leads; no code, text, aliases or presets embedded and no PS5 write compatibility inferred. |
| Cyberpunk metadata research | [steliosc custom-controls UserSettings.json](https://github.com/steliosc/Cyberpunk-2077-Custom-Controls-Guide/blob/main/UserSettings.json); reuse rights not relied upon | Research only. Self-describing option vocabulary corroborated against PS5 bytes; no source code/data copied. |
| Earlier byte reconnaissance | [Cyberpunk](../../docs/formats/PPSA04027.md), [Ridge Racer](../../docs/formats/PPSA07696.md), [roadmap research](../../docs/plans/GenericJson.md) | Context and additional primary research links; not authority for untested writes. |

No third-party reference dataset is bundled, so there is no adapted CC BY-SA/MIT data notice to carry beyond the source records above. References are also exposed through `Info.References`.

## SDK requests

F10, F5, F8 and F6 are consumed directly; no plugin-local substitute history, query or diff protocol was added. F7 is available but adoption remains evidence-gated as documented above.

- **Resolved:** tagged ExactNumber/Union review rendering, reported in [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18#issuecomment-6479348148), was fixed by `03b66b7` with shared tests. This round verified exact preview rendering in the browser and removed the obsolete Overview warning. No new blocking SDK request was found.
