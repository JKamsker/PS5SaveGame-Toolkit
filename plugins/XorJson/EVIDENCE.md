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
  unknown; game structural writes and all character writes stay disabled.

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

## Outstanding evidence

See the exact capture/reload checklist in [README.md](README.md). None of the
owner-dependent phase completion or console-maturity criteria has been met.
