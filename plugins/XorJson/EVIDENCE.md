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
