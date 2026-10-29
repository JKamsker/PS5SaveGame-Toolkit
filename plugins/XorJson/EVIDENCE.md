# XorJson evidence ledger (2026-10-28)

Only metadata and conclusions are recorded here. No sample bytes are included.

## F1 baseline

Input: `PPSA16064_saves_dec.zip` in the configured artifacts directory.
One generation (`g0`), two game files, no backup twins:

| Role | Bytes | SHA-256 |
| --- | ---: | --- |
| Character | 40,062 | `87066f990c7c24d4908773d0fd746f579b814b2508a6b098952507ea6ca58f54` |
| Global settings | 3,978 | `ea92d54c92ed7e194113edfc634173628795fc0cf0d95557c1ae49d2c187e335` |

```powershell
$sample = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts/PPSA16064_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- list $sample
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $sample $sample --before-gen 0 --after-gen latest --semantic auto --all
```

Observed baseline: 2 unchanged files; 0 changed/added/removed/churn-only files;
0 changed bytes; 0 semantic changes. This comparison aligns `g0` with itself.
No independent capture exists, so it proves no field's gameplay semantics.

## Format / read evidence

- Settings: byte-plus-one decoding succeeds; XOR-1 decoding fails. Seven named
  blobs: Audio, General, Gfx, Controls, KeyBindings, Interface, Accessibility.
- Character: plain JSON, `HardFormat=FCharacterSaveV1`, SoftVersion 2,
  FormatHash 3777917472, offline, level 1, 5 inventory entries, 9 attributes,
  49 quest records. Initial item effect arrays are empty. These are observed
  storage mappings, not verified write semantics.
- Seed plus occurrence is a candidate inventory identity. Stable session IDs
  are independent of row position. Persistence between game captures is unknown.
- No integrity field is apparent. External integrity and console acceptance are
  unknown. Existing scalar and bounded F5 structural writes are enabled for the
  tested offline character and settings shape with explicit warnings; no write is
  claimed as console-accepted.

## Automated differential over the sample

`SampleEvidenceTests.F1IdentifiesExactlyTheEditedSettingsTokenAndUndoRestoresSampleBytes`
uses the F1 engine and a test-only transform decoder. It changes the existing
`masterVolume` lexeme from `50` to `49.000E+00`. The only semantic change is
`blobs[name=Audio]/masterVolume`; the character and all other settings values are
unchanged. Undo restores the exact source bytes. This is an editor-generated
differential, **not** a console-generated before/after pair.

The decoder uses blob names for F1 alignment and preserves exact numeric text.
Its generic array fallback is positional; it is not evidence for inventory
identity across captures. Future nested seed alignment needs actual paired saves.

The Core fix `4532481` is now relied on directly. Round 2 removes the all-files
override workaround. The F1 test supplies only the settings output, asserts the
untouched character is byte-identical, and still observes exactly one changed
file/token. No Core file is modified.

## Round-2 read projections and catalog coverage

F8 tables were checked against the same PS5 bytes. Their values and labels remain
inferred even though existing scalar cells are now writable:

| Collection | Stored rows | Interpretation boundary |
| --- | ---: | --- |
| Attributes | 9 | AttributeName and exact CurrentValue; no caps or dependency claims. |
| Inventory | 5 | TypeTag, rarity, slots, stack, generated power; all Effects arrays empty. |
| Quests | 49 | QuestName, raw State, TaskData count and FocusedQuestId equality. |
| Tasks | 425 | TaskName, parent quest, raw State and exact PartialProgress. |
| World | 2 | One cutscene tag, one fog record with 256 Data elements; no coordinate/map interpretation. |
| Achievements | 32 | Stored completion/count/collected-tag data; no reward semantics. |
| Collected records | 3 | Two item tags and one hint record; no inferred collection completeness. |

The MIT source remains pinned to
`84ffee67b73e49d5b888e31257ce9767aa8ab826`. Pack 1.2.0 adds 30 enchantment IDs
and 926 task IDs to the existing data, with 42 short quest names and 502 short
task labels. Every pinned base/unique item ID, quest name and task label was
checked against the generated data. Only 2/49 PS5 quest IDs and 37/425 PS5 task
IDs match this source. Unmatched IDs and unknown fields stay raw and lossless.
This mismatch is why progression and picker writes remain Experimental and warn
that labels, dependencies and ranges may be wrong even when a field can be edited.

F8 tests query complete collections before expanding raw sections, check
filter/search over 2,001 synthetic rows, stable disjoint pages, deterministic
ties, duplicate IDs/ambiguous shapes, bounds and Core session writes/undo.
F4 tests use Core with both byte transforms, known/unknown original IDs,
escaped spelling, invalid replacements, exact output, reopen and undo/redo.
Those generic-format tests establish no real-character gameplay write safety.

Minecraft Wiki page/licence retrieval returned HTTP 403; no wiki text or assets
were imported. F7 merged on main as `7ff21ad`; XorJson now proposes same-title,
same-generation character/global groups, stages both outputs, prefixes identities
and restores one complete memento. Paired-file membership, shared dependencies
and console acceptance remain Unknown. The scalar bulk action uses F6
preview/apply and retains warnings; no game-aware preset is synthesized.

## Outstanding evidence

See the exact capture/reload checklist in [README.md](README.md). None of the
owner-dependent phase completion or console-maturity criteria has been met.

## Unlicensed-source round (2026-10-29)

F2 pack 1.3.0 adds original descriptions for all 60 existing effect IDs using
dungeons.tools PC 1.1.2.0 / Steam build 25754144 factual reference data. Source
HTML SHA-256: `fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6`.
There are 60 unique exact-name crosswalks, 60 compatible-gear counts and 180
tier display observations; the 16 remaining site records have no established
catalog-ID mapping and are omitted. Original MIT identifiers remain unchanged.

The actual PS5 sample still has five inventory entries with empty `Effects`
arrays. No populated PS5 effect record confirms these descriptions or display
percentages. `CommunityDescriptionTests` checks that boundary and byte-identical
sample output, source/hash metadata, picker/field provenance, and synthetic
effect edits/reopen/undo under both transforms with unrelated bytes preserved.
No tier display value became a serialized strength, validator or gameplay limit.

The lack of a published community licence is resolved under owner decision
2026-10-28. Verbatim game descriptions and publisher icons/art stay excluded;
MetaBot direct imports also remain excluded under its general NC terms. The
README exclusions table and `effect-notes.source.md` preserve exact source pins,
attribution and alternatives. Write warnings and Experimental maturity remain.

## Asset round (2026-10-29)

Plugin 0.5.0 / reference pack 1.4.0 adds 418 catalog icon associations
(388 distinct icons), 399 official PC descriptions (309 item, 60 effect,
30 enchantment), and one Overworld reference background. New image bytes:
974,007 (784,515 icons + 189,492 map). All image inputs were validated and
processed outside the repository with Python -I; no downloaded code was run.
Source URLs, input hashes, crosswalks, processing and publisher attribution are
recorded in the pack's game-assets.source.md and game-assets.sources.json.

The map source's native pixel transform is known, but its saved identifier
crosswalk is unverified. Markers remain unpositioned/Unknown with a text list;
Overworld art is explicitly a reference and may not depict a saved location's
region. There are no imported saved-location coordinates or gameplay changes.
Existing warnings, Experimental maturity and original MIT notices remain.

### First-load measurement

A temporary net10.0 Release console referenced Testing/Core and the plugin.
It imported PPSA16064 before timing and selected the largest non-system file
(40,062 bytes). Stopwatch and GC.GetTotalAllocatedBytes(true) measured creating
one ReferencePackStore, pinning its context, opening the document, BuildView,
and querying the first character/inventory page. The warm measurement repeated
Open/BuildView/Query 100 times against that same store/context, disposing each
document. Both measurements used the same code and external artifact directory.
The benchmark source/output stayed under %TEMP%/xorjson-assets/benchmark.

- Before: first load 132.907 ms, 14,747,720 allocated bytes;
  warm mean 4.274 ms, 2,755,751 allocated bytes per document.
- After: first load 153.595 ms, 50,376,336 allocated bytes;
  warm mean 5.745 ms, 2,773,732 allocated bytes per document.

These are quick single-process observations on a shared machine, not a stable
performance regression threshold. Startup includes the eager Core pack loader's
image validation and JIT; image bytes are cached once per host store. Warm
allocation increases by 17,981 bytes per document; no document copies or decodes
the image pack. Tests assert reference reuse across two documents. Making the
host loader lazy would require a separate Core change outside this plugin.

### Verification

With SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts
exported in the same shell:

- dotnet test --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests:
  84 passed, 0 failed, 0 skipped (including sample conformance).
- dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -v:q:
  0 warnings, 0 errors.
- Pack validation covers normalized catalog hashes, binary asset hashes,
  image types/dimensions and every assetId. New tests cover native picker
  image/text provenance, all catalog icons, byte budget, actual sample F3 images,
  cached asset reuse, map validation, missing-pack fallback and no-op bytes.

All repository edits stay under plugins/XorJson/. The central licence register
still has the prior publisher-asset refusal and needs the integrating agent's
update; this worker does not edit docs/ because of the assigned plugin boundary.
Console acceptance and populated PS5 effect evidence remain owner work (#31).
