# Generic JSON (`format.json`)

An exact UTF-8 JSON workbench with sample-backed title views and previewed existing-value adapters. **Experimental**: console acceptance is untested. The 2026-10-28 owner decision, “Enable writes. Keep warnings,” replaces the earlier evidence gates; validation is tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). `Info.Games` remains empty because detection is format-based. Generic JSON editing is unchanged.

## Coverage and evidence

| Title / role | Observed coverage | Interpretation |
| --- | --- | --- |
| PPSA04027 / Cyberpunk 2077 | `settings.json`, 45,415 bytes, version 136; 28 groups / 149 options; `metadata.9.json`, 2,610 bytes | File-derived metadata, bounded scalar editing, paired choice/index adapters and recorded-default resets. Binary `sav.dat` / `user.gls` are separate formats. Slot metadata uses the raw workbench. |
| PPSA07696 / Ridge Racer 2 PSP classic | `imgui_emu_option.json`, 1,320 bytes; `cubic_A = -0.5` | Bounded wrapper primitives and imported-value reset. `DATA.BIN` gameplay is separate. PS1 preset documentation does not prove PSP enum meanings. |
| PPSA02343 / It Takes Two (EU) | Three primary `ue4savegame.dpx.sav` generations, 3,993 / 3,522 / 1,312 bytes, plus byte-identical backup twins | String-preserving adapters for present `Minigame.*`, `MinigameFlag.*`, `MinigameCounter.*` and chapter/checkpoint keys, including present-key presets. Absent keys stay absent. |
| Other titles / filenames | Complete valid UTF-8 JSON, including root scalars/null | Generic workbench only. A title ID or `.sav` extension does not establish JSON or Unreal GVAS. |

Byte-level shapes, lengths and no-op preservation were recorded on 2026-10-28 and rechecked on 2026-10-29, with round-trip coverage in the current tests. Gameplay meanings and write dependencies remain **Inferred/Unknown**. Matchers require the title, file role and expected shape; Cyberpunk also requires the exact numeric lexeme `136`. Invalid or ambiguous required root/group keys disable the title overlay. Unsupported individual option metadata or wrapper child types can instead leave the overlay visible while declining that field's write adapter. Overlays are rematched after edits and undo.

Cyberpunk overlays allow at most 128 groups with string `group_name` and array `options` members. It Takes Two requires a unique object `Profile` containing only strings, at most 128 matching gameplay fields, and unique present `CloudSettings` / `LocalSettings` members; their value types are unconstrained. Ridge Racer requires unique object `options` / `settings` members, with arbitrary child JSON retained. These are matching rules, not complete game schemas.

F1/F12 `genericjson.title-values-v1` / `SemanticDiffer` tests compare the three It Takes Two primary generations, restricting output to aggregate changes in minigame/chapter/checkpoint path families. The production SDK hook replaces the test-only decoder workaround and excludes unrelated/private profile families. All six primary/backup occurrences round-trip identically. Archive order is not chronology: these snapshots do not prove a transition, counter unit, flag meaning or dependency. No identity values or private save payloads are committed/logged.

Friendly labels use original word splitting, not an external localization/alias dictionary. The small embedded F2 pack annotates six already documented Cyberpunk v136 difficulty/display IDs with original descriptions and categories; it contains no save payload, game art or copied guide prose. F4 browse-only controls expose search/category/sort, exact IDs and attribution, anchored to the imported ID to avoid duplicate non-revertible Changes entries; current values remain visible above. Planned edits use the same attributed F4 reference picker inside the F6 dialog, with exact raw IDs and eligibility limited to the imported choices. Without a matching pack, or for other recorded choice lists, the existing file-derived enum remains. Raw names, occurrence paths and quoted JSON string values remain visible. A matching Cyberpunk `values[index]` does not prove which paired writes the game accepts. Dynamic choices without recorded lists remain unknown. It Takes Two preserves flag casing and score strings; no identity, EULA or telemetry fields enter gameplay actions. Complete minigame aliases and chapter/checkpoint graphs remain owner-blocked.

The It Takes Two F9 location list shows only minigames with present profile records, always with Unknown completion/location state. Each marker navigates to a raw field, including through wide lazy ranges. An attributed external guide supplies location guidance on demand. No coordinates, artwork, missing-key completion state or prerequisite edges are invented. The three chapter/checkpoint strings do not establish a prerequisite DAG, so `GraphNode` is deliberately absent.

## Framework feature adoption

| Feature | Adoption | Where / concrete limit |
| --- | --- | --- |
| F1 SaveDiff | Used | ByteDiffer independent token-patch oracles and occurrence-aware generation diffs; production title hook below, integrated in `01657c2` (2026-10-28). |
| F2 reference packs | Used | `genericjson.cp2077.v136`: difficulty/display facts, build/title pinning, attribution and Sources usage tracking; cached catalogue resolution, integrated in `01657c2` (2026-10-28). |
| F3 rich cells | Used | Search/array rows contain editable exact fields, collapsible container details, reset actions and confidence metadata. The only catalogues are six settings choices, with no verified native icon associations; see the asset assessment below. |
| F4 reference pickers | Used | ReferenceSpec for the actual F6 value parameter of both catalogued Cyberpunk settings, with action-aware search, attribution and imported-choice eligibility. Independent browse-only controls remain anchored to the imported ID. Missing packs/unknown list variants retain raw/recorded enum access. |
| F5 reversible structure | Used | Array insert/remove/move/duplicate and object insert/remove/rename, immutable IDs, snapshots, exact undo and schema/type templates. |
| F6 planning/review + #27 | Used | All title writes/presets and generic structure/reset actions preview; catalogued settings use the F4 picker for their value parameter and preview both value and derived index. Selected/all-matching query scopes support exclusions and one-step undo. Direct-array scopes integrated in `01657c2` (2026-10-28). |
| F7 document groups | N/A | Every supported write changes one JSON file. No cross-file invariant has been identified; unrelated co-located files and identical backups do not define a transaction. |
| F8 queryable lists | Used | Whole-tree search plus direct-child collections for every array over 100 entries, pages capped at 200. Raw lazy navigation remains available. Direct-array browsing integrated in `01657c2` (2026-10-28). |
| F9 spatial views | Used | External-only present-minigame MapNode with raw-field navigation and full text alternative, integrated in `01657c2` (2026-10-28). Graph prerequisites/coordinates are unknown; no graph edges or positions are guessed. |
| F10 exact/nullable/union | Used | Exact numeric lexemes, explicit scalar choices and null; no double conversion. Out-of-budget original numbers remain verbatim read-only. SDK-06 supplies the imported lexical spans and unchanged-topology scalar patch path. |
| F11 shared codecs | N/A | These inputs are uncompressed UTF-8 JSON; no Kraken, SQLite or private codec implementation exists. |
| F12 bounded decoder hook | Used | SDK-only `genericjson.title-values-v1`, explicit CLI selection, sink depth/value/byte limits; test evidence uses this production hook, integrated in `01657c2` (2026-10-28). |
| F13 inspection/browsing/sort/duration | Used | Nonmutating aggregate syntax inspection; browse-only F4 and catalogue filters/sorting, integrated in `01657c2` (2026-10-28). Float duration: N/A — no field has proven time units, so formatting would fabricate semantics. |

Shared helper adoption: `BoundSaveDocument` supplies bindings, atomic scalar edits and F5 snapshots;
SDK-06 `LosslessJsonDocument` supplies the strict shared lexical parse and scalar span replacement
for unchanged topology. GenericJson retains its `value/{Index}` identities, member/trivia spans,
structural writer, title adapters and structural snapshots. `ExactJsonNumber` supplies F10 validation;
oversized imported lexemes remain local read-only text. The newer `ActionPlans` helpers are not consumed:
`ApplyActionPlan` replans locally and compares ordered changes before applying them. `InMemoryCollection<T>`
is not consumed either: the JSON query engine retains its complete-tree/direct-child traversal,
allocation-bounded page rendering and 10,000-match sort limit. Catalogue annotations use cached direct
`Catalog` / `Find` calls, rather than `ReferenceCatalogReader`. Binary cursor/range/FString/CRC helpers
are inapplicable to these JSON files; `ReferenceViews.Icon` has no embedded images to resolve, and the
local aggregate inspection action does not inspect a reference record through `ReferenceInspection`.

### Native evidence decoder

`savediff compare before.zip after.zip --decoder genericjson.title-values-v1` selects the explicit SDK hook (listed by `savediff list-decoders`). It matches the same three title/role/schema profiles, preserves JSON scalar types/exact lexemes and emits no action parameters or UI display cells. Cyberpunk paths use escaped group + option name + occurrence, independent of option array order. Profile keys retain duplicate occurrence suffixes; wrapper arrays use positional fallback because no native record identity is established. Such fallback is explicitly warned. Sink exhaustion stops traversal; file and depth caps mark incomplete output. Missing paths in truncated evidence do not prove removal. The hook is read-only, stateless and performs no I/O.

## Enabled writes and their evidence

All title write families below use F6 preview/apply, including the single-field adapters. Every successful title preview and apply outcome carries: **“Not validated on console. The game may reject or overwrite the edited save. External integrity and game dependencies are unknown.”** Parameter controls retain Inferred/Unknown confidence and file-derived provenance. A preview is pure; apply revalidates it, patches existing raw value IDs and creates one undo group. No alias bindings, replacement history or local transaction protocol were added. Owner validation is tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Cyberpunk scalar bool/number/text | Inferred | v136 file-recorded type/min/max/step; sample-backed adapter edits | JSON reparsed; external integrity unknown | Change one option, verify menu effect, restart/resave/reload/export |
| Cyberpunk recorded choice + derived index | Inferred | 24 sample choice lists with matching value/index; occurrence IDs preserve group/name distinctions | Both tokens patched atomically and reparsed; external integrity unknown | Determine value/index authority and overwrite behavior |
| Cyberpunk dynamic raw text + explicit index | Unknown | Existing type and imported text length/index only; no fabricated choice list | JSON reparsed; external integrity and choice availability unknown | Capture valid language/HDR choices and index domains |
| Cyberpunk per-option/bulk recorded defaults | Inferred | 144 options record a scalar default or default index; unsupported/malformed defaults are skipped | Paired indices included; JSON reparsed; external integrity unknown | Compare toolkit defaults with native menu reset on this build |
| It Takes Two present string booleans | Inferred | Gameplay key families; `true`/`True` observed; per-field lower/title/upper style preserved | JSON string storage retained and reparsed; external integrity unknown | Discover/play separately; verify casing and actual effect |
| It Takes Two present string counters | Inferred | Digit strings; family maximum 67 across the supplied generations | JSON strings retained and reparsed; units/limits/external integrity unknown | Separate Cody/May wins/scores; verify units/caps and zero behavior |
| It Takes Two chapter/checkpoint strings and explicit three-field tuple | Inferred | Existing `FurthestUnlockedChapter`, `LastSaveChapter`, `LastSaveProgressPoint`; PC editor corroborates names only | Existing strings patched as one group; progression dependencies/external integrity unknown | Continue, Chapter Select, replay and forward progression after restart |
| It Takes Two present-flag preset (true/false) | Inferred | Same existing boolean adapters; no missing key insertion | One F6 undo group; JSON reparsed; story dependencies/external integrity unknown | Selected/full present flags; rewards/availability and unrelated story state |
| It Takes Two counter-zero preset | Inferred | Same existing counter adapters; zero is an explicit unverified target | One F6 undo group; JSON reparsed; external integrity unknown | Zero each score/win type separately and verify native resave behavior |
| Ridge Racer wrapper primitive edits | Unknown | Imported types/values only; no PS1 enum meanings transferred | JSON reparsed; no DATA.BIN changes; external integrity unknown | Filter/upscale/AA/controller/rewind effects on exact PSP wrapper build |
| Ridge Racer restore-imported preset | Unknown | Exact imported scalar values; this is not a game-default/visual preset | One F6 undo group; original spellings restored; external integrity unknown | Native wrapper restart/resave and compare all companions |

Generic writes are also enabled for every accepted JSON document, including unrecognized titles:
direct raw scalar edits/type changes (`Unknown`), previewed array insert/remove/move/duplicate and
object insert/remove/rename, and previewed restoration of existing imported scalars. The structural
controls use explicitly supplied indices/names/templates, without title semantics or a game schema.
Their syntax/preservation evidence comes from synthetic, conformance and sample round-trip tests;
outputs are reparsed against the live token model, with external integrity unknown. Direct scalar
edits use normal field editing; F6 preview/apply is required for structure and reset actions. The
`Verified` child-sequence fields are read-only structure evidence, not a game-write confidence claim.

Cyberpunk integer settings enforce integral values, min/max and recorded steps relative to minimum, allowing recorded endpoints. Float min/max remain exact; float steps are advisory because PS5 metadata such as `0.0099999997` does not divide its own defaults/endpoints exactly. Decimal arithmetic is used only after an exact equality check prevents rounding; JSON number lexemes are never converted through binary floating point. Edited metadata is not silently accepted as a new schema: changed membership/order/names of an option object's members, or changes within its metadata subtrees, disable that adapter and make presets skip it. This includes removed steps and reordered/removed choices; structural undo restores eligibility and exact imported bytes. This does not prohibit reordering whole option records. Generic syntax editing remains available. Dynamic text uses at most the imported length (hard ceiling 4,096); explicit indices use `0..imported index` and carry an Unknown warning.

Profile counters use `0..max(imported count, 67)` within Int32, a conservative observed family range, **not a proven score/win cap**. Other text adapters use at most the imported length (hard ceiling 4,096). Wrapper numbers range between zero and the imported number; an imported integer remains integral. Zero-valued unknown enums therefore have a single-value semantic range. Numeric-looking wrapper strings use `0..imported count`, independently of the profile counter range. Raw workbench controls retain their existing broader syntax-only behavior.

Defaults/flag/counter/imported-wrapper presets support document scope, selected F8 raw value rows and resolved “all matching” queries. Empty selection means no edits; noneligible rows are listed as skipped. A selected Cyberpunk value also changes its paired index as a derived effect. The chapter tuple requires all three existing, unique fields and document scope. It does not validate a story graph or invent companion state. Single-field/default actions also require document scope. Title previews are limited to 5,000 changed/skipped fields. Adapter discovery examines only the first 5,000 candidate options/leaves; when that prefix is truncated, document-scope actions reject and bounded selections can use eligible adapters within the prefix. Later fields retain raw JSON access.

## Exact values and preservation

Raw workbench scalars use F10 `NullableSpec(UnionSpec)` with explicit exact-number, string and boolean choices. A present null, an absent property and an empty string remain distinct. Multiline strings are supported. Unknown fields remain available through the raw workbench with `Confidence.Unknown`. The overview, raw scalar fields and generic structural/reset controls carry: **“Not validated on console. The game may reject or overwrite the edited save. External integrity and game-specific dependencies are unknown. JSON validity does not establish game compatibility.”** Structural/reset previews and successful outcomes retain this warning; structural actions also retain their membership/name/order and template qualifications. Read-only resource-limit fields retain their specific preservation warning. Title action parameters use the appropriate scalar/enum/reference spec and preserve the underlying JSON type.

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

Template schemas apply only to the inserted fragment during preview/apply. They are not inferred title schemas, persistent document constraints or evidence of game-safe values. Cross-container moves, complete JSON Schema and built-in title insertion schemas remain unimplemented. Present-value presets above do not create absent game keys.

## Companion files and F7

F7 document groups (#29, merged as `7ff21ad`) were reviewed. No current GenericJson title has proven coordinated JSON writes, so this plugin remains a single-file document and emits only its opened file. Cyberpunk settings, slot metadata and binary gameplay coexist but have no established cross-file write dependency. It Takes Two's `Profile`, `CloudSettings` and `LocalSettings` are sections of one file, not separate companions. Ridge Racer's wrapper options and binary `DATA.BIN` have different roles without a proven coordinated edit. Identical backup twins alone are not evidence for a new group; ordinary export mirroring remains the host's responsibility.

The write-enable round reviewed these roles again: none of its enabled writes needs a clearly coupled companion, so no F7 group is declared. This is an absent invariant/group implementation, not a console-evidence gate on the enabled edits. A future group must identify the cross-file invariant, pin exact file roles and fingerprints, reject ambiguous/missing partners and explicitly include backups if owned. See [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md).

## Resource bounds

| Operation | Bound / behavior |
| --- | --- |
| Parse / export | 16 MiB UTF-8, 250,000 value nodes, depth 64; optional preserved UTF-8 BOM. Comments, trailing commas, extra roots, malformed Unicode/UTF-8 and UTF-16 reject. |
| Lazy navigation | At most 128 child nodes per expansion; wide containers use hierarchical ranges. |
| Search / array tables | At most 200 rows per request; unsorted queries retain only the requested page. Arrays over 100 entries have stable direct-child collections; unfiltered array pages address only the requested slice. Sorting requires at most 10,000 matches and computes each key once. Displayed paths/original literals are clipped; complete scalar controls and full-key/value search remain available. |
| Array/object insert | At most 1 MiB of template characters, plus normal parse/export bounds. New object names: at most 16,384 UTF-16 characters. |
| Explicit template schema | 64 Ki UTF-16 characters, JSON depth 16; generation at most 4,096 nodes and 128 generated entries per array (larger explicit defaults still obey total bounds). No network/reference resolution. |
| Action review / reset | At most 5,000 affected review fields; host plan-size and query-selection bounds also apply. |
| Structural source retention | Serialization rejects more than 32 MiB of distinct source buffers referenced by live nodes. Exporting and opening a new document consolidates those buffers; closing the old session releases its history. Core separately bounds undo snapshots. |
| Changes panel | Core traversal/resource limits apply (currently 100,000 visited view nodes and depth 128, plus its configured baseline byte budget). Large/deep review may be incomplete even though full-tree search still works. |

Malformed input is rejected by `Open` with the documented SDK `SaveFormatException`; `Probe` returns no match. UTF-8 failures report an absolute zero-based byte offset. Syntax/depth failures report a one-based line and zero-based byte position within that line (after removing a leading UTF-8 BOM). Invalid escaped surrogate pairs report the containing token's absolute byte offset. Messages contain no input excerpts. Only one UTF-8 BOM at the start is accepted; repeated, misplaced, truncated, UTF-16 and UTF-32 BOM variants reject.

Duplicate object names, including equivalent escaped names, are intentionally accepted and preserved as distinct occurrences with distinct value IDs. Valid numbers beyond SDK lexeme/exponent budgets remain verbatim and read-only; they are never converted to floating point or expanded by exponent. These are lossless workbench policies, not title-schema validation. Unicode-string rejection follows the [.NET `Utf8JsonReader.GetString` contract](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader.getstring?view=net-10.0), alongside the existing [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) syntax reference; no upstream code was copied.

## Validation

Action-parameter picker integration, 2026-10-29: Release GenericJson suite **422 passed, 0 failed,
33 sample-dependent skips** (455 total); targeted reference tests **12 passed, 0 failed, 2 skips**.
Synthetic host-session tests cover action/value reference lookup, attributed search/category/sort,
imported-choice eligibility and order, unavailable imported IDs, enum fallback, nonmutating browse/
discarded preview/no-op/cancellation, coupled value/index writes, exact one-step undo/redo, clean
original/current review and stale revisions. No private samples were used. Browser smoke remains
for separate integration validation: this isolated worktree has no built frontend or API host.
Console acceptance, confidence and maturity remain unchanged.

Docs accuracy and allocation-test stabilization, 2026-10-29 (#40): ten consecutive sample-backed
Release suites each passed **433 tests, 0 failed, 0 skipped**. Documentation checks passed all
14 cases; API Release build had **0 warnings, 0 errors**. Aggregate sample sizes/counts and pack
size were rechecked without logging private values. Production code, IDs and confidence labels
are unchanged. The 50,000-element opening test keeps its **44,000,000-byte** bound, using one
warm-up and the minimum of three measured opens/first views in an isolated test collection.
The ten asserted minima ranged from **35,465,232 to 35,465,440 bytes**; see
[measurement details and reproduction](PERFORMANCE.md#allocation-test-stabilization--2026-10-29).

Maintainability review, 2026-10-29 (#40): **433 passed, 0 failed, 0 skipped** (402 existing cases retained,
31 added characterizations); API Release build **0 warnings, 0 errors**. Named parsing, planning,
query-rendering and decoder phases replace the six production methods over 50 lines. Current token-type
classification and test-only view traversal are shared. All 1,536 baseline/current sample view, expansion,
page, semantic-plan and decoder fingerprints match; all 12 recognized sample files retain byte-identical
no-op exports. Duplicate-key/huge-number coverage and existing allocation bounds remain unchanged.
One intermediate full run exceeded the 44 MB opening budget (44,756,936 bytes); the unchanged confirmation
run and isolated allocation suite passed. External before/after allocation measurements converge to the
same 35,465,4xx-byte steady-state range; no threshold or production runtime setting was changed.

Performance review, 2026-10-29 (#40): **402 passed, 0 failed, 0 skipped**; API Release build **0 warnings, 0 errors**.
Six findings fixed, with allocation/call-count regressions and unchanged view/page fingerprints.
Largest-sample Open + first view: **3,393,344 → 2,092,920 allocated bytes**; 50k-array deep whole-tree page:
**8,439,632 → 41,952 bytes**. See [measurements, reproduction and preservation checks](PERFORMANCE.md), including
the primary .NET reader/Unicode API sources used for streaming validation. Byte-identical no-op exports,
duplicate-key occurrences, huge numbers and all original test cases remain covered.

Malformed-input review, 2026-10-29 (#40): **267 passed, 0 failed, 0 skipped** with private samples configured (baseline: 204); API build **0 warnings, 0 errors**. The 63 added synthetic cases cover every truncated prefix of representative nested/BOM/Unicode documents, invalid UTF-8 in values/member names/trailing data, malformed syntax and surrogate escapes, BOM variants, 10,000-level nesting rejection, exact depth/node/byte boundaries, huge integer/fraction/exponent preservation, and escaped duplicate-name occurrence edits. Five diagnostic regressions failed before the fix; the remaining 50 initial corruption cases already passed. No wall-clock assertions or sample payloads were added.

Completion validation, 2026-10-28: **198 passed, 0 failed, 0 skipped** with private artifacts configured; API build **0 warnings, 0 errors**. New tests cover the 250,000-node bound with a large exact-number array, depth 63/64/65 and truncation, direct array paging/live selection/exclusions/reset/undo, retired collections, native decoder limits and deterministic real samples, pinned pack attribution/version/fallback, atomic picker-choice/index edits, nonmutating inspection/history, stable native map identities and every real profile map target. No timing assertions are used. Structural hashing streams its input, occurrence dictionaries are allocated only for objects, and valid numeric lexemes are decoded once. Private samples are not copied into the repository.

The completion round's unchanged frontend production build and **164 tests across 29 files** passed in an isolated TEMP copy. Synthetic browser validation confirmed browse-only catalogue details/attribution without commit controls, the F6 enum fallback previewing both raw value and derived index, apply/export, and one undo returning byte-identical original input. F9 Jump opened/focused the raw field with zero external guide requests; direct F8 array pages loaded and advanced to page 2. This historical run preceded the action-parameter picker migration described above; it does not validate that picker. No console acceptance is claimed.

Write-enable validation, 2026-10-28: **180 passed, 0 failed, 0 skipped** with private artifacts configured; API build **0 warnings, 0 errors**. Every supplied primary/backup generation exercises editable semantic adapters through the host's F6 plan/apply, serialize/reopen and undo/redo. Tests independently patch only planned source tokens, then use F1 `ByteDiffer` to require identical output, proving unrelated bytes remain intact. Presets include real sample defaults, present flags, counter zero, explicit progress tuples and imported-wrapper restoration. Single-value conservative ranges remain unchanged rather than inventing alternatives. Synthetic regressions cover invalid bounds/steps, exact decimal arithmetic, approximate float defaults, malformed metadata/overflow, duplicate keys, dynamic pairs, stale previews, wrong scopes/types, selected/all-matching/empty selections and bounded coverage. Existing generic F5/F10 conformance remains green. No new browser or PS5 console validation is claimed for this write-enable round.

The dated runs above record their respective revisions, rather than the current test count. Run
sample-backed tests with these environment variables in the **same shell command**:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP='D:/Temp/saveeditor/test-scratch/docs-accuracy-genericjson'
$env:TEMP=$env:SAVETOOLKIT_TEST_TEMP; $env:TMP=$env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests -c Release
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
```

Round-2 validation, 2026-10-28: **139 passed, 0 failed, 0 skipped**; API build **0 warnings, 0 errors**. Tests cover sample conformance/backup twins, content detection, conservative overlay fallback, richer read-only overlays across every generation, exact tagged host wire values, null/missing/empty/type changes, atomic invalid-input/resource rejection, collapsed 40,001-node search, imported comparison/subtree filters, lazy container navigation, baseline preservation, selected resets, structural boundaries, object rename/insert/remove, nested duplication, duplicate keys, stale requests, identity retirement, schema validation/generation/budgets and mixed undo/redo. Tests skip when private samples are absent; a skipped run does not count as sample validation.

The existing shared frontend suite passed: **97 tests in 13 files**. TypeScript/Vite production build passed. Dependencies, frontend copy/build output and browser workspaces were isolated under TEMP; no shared source files were changed.

Round-2 browser validation used only synthetic BOM-prefixed JSON. The normal object insertion dialog generated an explicitly supplied object schema containing a long exact exponent number; preview showed its exact typed value and absence correctly. Applying and downloading preserved both original and inserted lexemes and the BOM. Search by container identity opened its lazy subtree and exact-number control. One undo restored byte-identical input, with complete F6 review reporting zero changes. The temporary API and owned browser tab were closed. No console load/restart/resave has been performed.

## Roadmap status and evidence gates

| Plan phase | This round |
| --- | --- |
| 1: read-only interpretation | Title/role/shape matching, richer file-derived option metadata, word-split names, grouped profile flags/counters, provenance, F1 comparison and all sample/twin coverage shipped. Complete localization/alias/checkpoint catalogues remain open. |
| 2: exact JSON workbench | F10 exact lexemes, nullable/union controls, authoritative validation, wire/session/browser checks shipped. |
| 3: existing-value editing and review | F8 search/review plus enabled title scalar, paired choice/index, string-preserving flag/counter and chapter/checkpoint adapters. Warnings remain; owner validation is tracked in #31. |
| 4: reversible structure | Previewed array insert/remove/move/duplicate, object insert/remove/rename, explicit local-schema/type templates and mixed history shipped. Built-in title schemas and cross-container moves remain open. |
| 5: presets / complete reference packs | F6 recorded-default, present-flag, counter-zero, explicit progress tuple and imported-wrapper presets shipped. Complete reference catalogues, absent-key creation and named visual/unlock-all presets lack definitions/dependency repair and remain unimplemented. |

Owner capture checklist for validating enabled writes and future compatibility claims (no longer a write gate):

1. Record title/region, game build, wrapper/settings version, exact file roles and action for every pair. Export immediately before and after **one** change; retain all companions and duplicate/backup occurrences privately. Record unchanged control captures to identify churn.
2. Cyberpunk: capture one static enum change; one scalar change and reset-to-default; one dynamic language/HDR choice. Restart, resave and re-export each. Include a second settings version to test schema fallback and determine `value`/`index` authority.
3. It Takes Two: capture a fresh profile; discover one minigame; play it; separate Cody and May wins/scores; enter a chapter; cross one checkpoint. Capture incomplete and complete profiles on the same version, preferably from two owners. Record Continue/Chapter Select/replay behavior; do not use archive order as chronology.
4. Ridge Racer: capture before/after each filter/upscale/AA preset and one controller/rewind option, recording the PSP wrapper build. Restore, restart and re-export to establish numeric enum domains.
5. On spare saves, validate each proposed edit's exact menu/game effect, restart/resave/re-export, then compare unrelated state and all companions. Only the owner promotes maturity to Stable. No trophy, cross-platform conversion or arbitrary raw edit compatibility is promised.
6. For any candidate companion dependency, retain all files from the same imported generation and backup set. Record the minimal cross-file invariant, run F1 with unchanged controls, and test whether a single-member edit is rejected/overwritten while the coordinated edit survives restart/resave. Co-location alone does not authorize F7 grouping.
7. Supply owner-verified menu labels/localization, complete chapter/checkpoint paths and minigame aliases with exact raw-key mappings. For a built-in title template, identify each required/optional member, legal default, array limit and dependency using controlled pairs; test creation on a fresh/incomplete spare profile. Do not include account IDs, credentials or private payloads in public notes.

The detailed capture checklist and remaining validation needs are maintained in [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md).

## Sources and attribution

| Dataset / implementation | Source / author / rights | Use |
| --- | --- | --- |
| Cyberpunk v136 choice annotations | [Documented byte reconnaissance](../../docs/plans/GenericJson.md), PS5 Save Game Toolkit contributors; original GPL-3.0-or-later annotations ([licence](https://www.gnu.org/licenses/gpl-3.0.html)), rechecked 2026-10-29 | Six documented raw IDs; original categories/uncertainty notes/word splitting. [Sidecar](SaveToolkit.Plugins.GenericJson/ReferencePacks/genericjson.cp2077.v136/choices.source.md); F2 Sources and F4 field attribution. Imported choices stay authoritative. |
| Cyberpunk Performance Mode description | [Official patch 1.5 release notes](https://www.cyberpunk.net/en/news/41435/patch-1-5-next-generation-update-list-of-changes), CD PROJEKT RED, 2022-02-15; retrieved 2026-10-29. Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29 | One verbatim sentence in the `Performance` catalogue description, with historical context and an explicit inferred raw-ID association / unverified installed-build warning. Display dataset and record provenance appear in F2 Sources / F4 details. `Quality` is not relabelled Ray Tracing. [Sidecar](SaveToolkit.Plugins.GenericJson/ReferencePacks/genericjson.cp2077.v136/choices.source.md). |
| Minigame location guide | [PowerPyx](https://www.powerpyx.com/it-takes-two-all-minigame-locations/), 2021-03-26; checked 2026-10-29. Earlier rights-only exclusion resolved by owner decision 2026-10-29 | Attributed external link in F9 because no aligned overview map/coordinate mapping was established. No guide text, image, coordinates or alias table imported. Markers/names are file-derived original interpretation ([sidecar](SaveToolkit.Plugins.GenericJson/JsonProfileMap.source.md)). |
| JSON grammar | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259), IETF | Standard reference; no implementation copied. |
| Token parser | [Utf8JsonReader](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader), Microsoft / .NET runtime | Runtime API; original token/trivia model and writer. No community parser code copied or executed. |
| Explicit template vocabulary | [JSON Schema reference](https://json-schema.org/understanding-json-schema/), JSON Schema project; specification/reference only | Original bounded subset implementation; no documentation text, example, code or external schemas copied. [Provenance and restrictions](SaveToolkit.Plugins.GenericJson/JsonTemplates.source.md). |
| Title identification | [PROSPEROPatches PPSA02343](https://prosperopatches.com/PPSA02343), PROSPEROPatches; no reuse licence established | Research only. No catalogue text, artwork or dictionary embedded. |
| Wrapper research | [PlayStation-PS1HD](https://github.com/andshrew/PlayStation-PS1HD), andshrew, MIT | Research only; no code, preset table or prose adapted. PS1 meanings are not applied to PSP values. |
| Overlay names / shapes | Owner-supplied private samples; [provenance sidecar](SaveToolkit.Plugins.GenericJson/JsonOverlay.source.md) | Raw keys and metadata come from each imported file; friendly labels use original word splitting. Original matcher/prose; no distributed save payload or game dictionary. UI shows provenance. |
| Cross-platform editor research | [John's It Takes Two editor](https://johnssaveeditors.com/games/it-takes-two/); no reuse licence established | Research only, rechecked 2026-10-28. PC string/profile field names corroborate investigation leads; no code, text, aliases or presets embedded and no PS5 write compatibility inferred. |
| Cyberpunk metadata research | [steliosc custom-controls UserSettings.json](https://github.com/steliosc/Cyberpunk-2077-Custom-Controls-Guide/blob/main/UserSettings.json); reuse rights not relied upon | Research only. Self-describing option vocabulary corroborated against PS5 bytes; no source code/data copied. |
| Earlier byte reconnaissance | [Cyberpunk](../../docs/formats/PPSA04027.md), [Ridge Racer](../../docs/formats/PPSA07696.md), [roadmap research](../../docs/plans/GenericJson.md) | Context and additional primary research links; not authority for untested writes. |

The embedded choice pack combines original GPL-3.0-or-later factual annotations with the attributed publisher sentence above. References are also exposed through `Info.References`.

## Asset assessment (2026-10-29, issue #18)

- **Cyberpunk settings:** the only catalogues contain four difficulty IDs and two display-mode IDs;
  no item, skill or equipment catalogue exists in this JSON profile. Skip icons: no verified native
  icon-to-ID joins were established. The [Cyberpunk Wiki character-customization](https://cyberpunk.fandom.com/wiki/Cyberpunk_2077_Character_Customization) search results show
  difficulty illustrations for displayed names that differ from the raw IDs (`Story`, `Easy`,
  `Hard`, `VeryHard`); the page could not be retrieved for direct asset verification. Those images
  were not downloaded or assigned by ordinal position. The official release notes support one
  Performance-mode description. They call the other mode Ray Tracing, without proving its join to
  raw `Quality`; that description and difficulty descriptions remain unimported.
- **Ridge Racer wrapper:** emulator options and JSON primitives only; no gameplay catalogue worth
  illustrating. Skipped.
- **It Takes Two:** present profile keys/counters only; no catalogue of verified minigame aliases.
  PowerPyx supplies gameplay screenshots and route guidance, not an overview background with known
  bounds/transform. Searches for It Takes Two minigame overview/interactive maps did not establish
  a suitable map. Keep the existing external guide and text list; do not place markers on screenshots.
- **Scope/budget:** 0 icons, 1 official description, 0 backgrounds; **0 new embedded image bytes**.
  Reference JSON is minified, versioned and hash-validated. The existing pack store loads once;
  catalogues remain cached and are resolved when title adapters are first requested (including the
  initial view). Repeated views and unchanged previews reuse the adapters and catalogue lookups.
  See [asset-round validation](ASSET_ROUND.md) for measurements and test results.

## Licence exclusions

| Source / revision | Licence found | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- |
| [PowerPyx It Takes Two minigame guide](https://www.powerpyx.com/it-takes-two-all-minigame-locations/), published 2021-03-26; rechecked 2026-10-29 ([sidecar](SaveToolkit.Plugins.GenericJson/JsonProfileMap.source.md)) | Rights reserved by PowerPyx and Hazelight Studios / Electronic Arts (game imagery); used with attribution per owner decision 2026-10-29 | Resolved: historical rights-only exclusion lifted by owner decision 2026-10-29. No guide text, images, coordinates or aliases imported; no aligned overview map/coordinate mapping established. | Attributed on-demand guide link and file-derived F9 text list. | Rights-only refusal is resolved. Embedding a background still needs an applicable overview map and verified bounds/transform; do not position markers on gameplay screenshots. |
| Unspecified icon sources (historical blanket entry; no specific source/revision recorded) | No source-specific licence recorded; no licence or rights holder inferred. | Resolved: historical blanket rights-only exclusion lifted by owner decision 2026-10-29. No icons imported; applicable catalogues/native icon-to-ID joins remain unverified (see Asset assessment). | Original text/raw IDs and attributed choice descriptions. | Rights-only refusal is resolved. Identify the exact source, holder, revision and terms, then verify an applicable catalogue and native icon-to-ID joins before importing. |

No licence-based refusal remains for these assets. Remaining omissions are evidence gaps, not
licence incompatibility. The central historical register is outside this plugin's authorized editing scope.

## SDK requests

F10, F5, F8 and F6 are consumed directly; no plugin-local substitute history, query or diff protocol was added. F7 is available but no clearly coupled companion write is identified, as documented above.

- **Resolved:** tagged ExactNumber/Union review rendering, reported in [issue #18](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/18#issuecomment-6479348148), was fixed by `03b66b7` with shared tests. Exact preview rendering works in the browser; no obsolete warning remains.
- **Resolved and adopted:** shared planned-action reference parameters shipped in `63580dc`. `JsonSemanticTarget` now publishes its existing `ReferenceSpec` directly for the F6 value parameter; current `ActionParameterFields.tsx` passes the parameter value ID and action ID to `FieldControl`. Plugin integration tests query that action/parameter pair through `DocumentSession`, enforce imported choices and verify paired writes/history/review. The separate F4 field remains browse-only and imported-ID anchored. The file-derived enum fallback remains for absent matching packs; migrating it needs concrete
  consumer value, rather than a blanket helper requirement.
- **Resolved by a query-string route:** the API now supports `GET .../collections?collectionId=...` for IDs containing `/`; see [API routing guidance](../../docs/API.md). The earlier encoded path-segment failure does not require changing this plugin: GenericJson continues to ship `json-array-<immutable-id>`, with its existing stable identities and working F8 features.

The [central SDK request triage](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/SDK_REQUESTS.md)
records those fixes as SDK-19/20/21. SDK-06 is adopted through reviewed dependency commit
`72490fe8756627013c73c0b80fa874b563a0e652` (PR #51), now merged on main: shared parsing and scalar span patching are
used only when topology and member names remain imported. The streaming validator remains for
Probe, structural equality and exact diagnostic mapping; GenericJson retains its local structural
trivia writer, stable IDs, snapshots, title semantics and warning labels. SDK-06 has no structural
API, so no working GenericJson structural feature was removed.
Scalar length preflight follows the unchanged default serializer, including six-byte quote,
plus and backtick escapes. Serializer-oracle tests cover all ASCII code points and mixed Unicode;
exact-limit batch/history and near-limit allocation tests cover that preflight's consumer boundary.
