# Catalogue and schema evidence — 2026-10-28, updated 2026-10-29

## Scope and confidence

PPSA01325 only. There are two distinct primary `memory.dat` generations and two
byte-identical backup twins, not four independent gameplay captures. Build, region,
capture actions and chronological order are unknown. No edited save has been tested
on PS5. The plugin remains Experimental; catalogue labels are read-only and ownership actions
are now enabled with explicit warnings under the owner's write-round decision (see #31).

No private payload or account metadata is committed. The original catalogue round
contained no media; the current publisher pack includes attributed screenshot derivatives
and short official captions under owner decision 2026-10-29. See the README's
[publisher-assets inventory](README.md#publisher-assets-050--owner-decision-2026-10-29)
and the pack's `assets.source.md` for rights, sources and transformations.
`fixtures.json` stores exact schema paths and non-sensitive primitive
expectations, as supported by F1. It is explicitly observational, not a controlled
capture manifest. No churn rules are asserted without a control pair.

## Structural fingerprint

Both generations and twins have:

- 785 elements, 508 primitive values, no attributes.
- 50 artifact, 96 puzzle and 18 prop records, all retained as saved.
- SHA-256 `e412d790cf868baf5dd17d332fd3e025032588a34eb569e1a85487f98e55eb40`.
- No Armadillo special-bot keys; newer artifact/bot schemas are unsupported.

Fingerprint algorithm: recursively enumerate every element's occurrence-qualified
tokenizer ID as `element:<id>` and every primitive/attribute ID as `value:<id>`;
sort all lines with ordinal comparison, join with LF and no final LF, encode UTF-8
and SHA-256. Values, offsets, original spelling, whitespace, NUL and opaque suffix
are excluded. Empty elements, repeated sibling occurrences and attribute names are
included. A matching fingerprint describes structure/key sets, never a patch number
or evidence that every gameplay dependency is understood.

Reference records require this exact fingerprint and a build-agnostic dataset.
Missing packs, foreign builds, new schema keys and duplicate collectible keys fall
back to raw identifiers without inserting/removing/normalizing anything.

## Differential-save observations

F1 command (report output belongs in private scratch, never in the repository):

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$scratch='D:/Temp/saveeditor/test-scratch/docs-accuracy-astroplayroom'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:SAVETOOLKIT_TEST_TEMP=$scratch
$env:TEMP=$scratch; $env:TMP=$scratch
dotnet run --project tools/SaveToolkit.SaveDiff -- compare "$env:SAVETOOLKIT_ARTIFACTS/PPSA01325_saves_dec.zip" "$env:SAVETOOLKIT_ARTIFACTS/PPSA01325_saves_dec.zip" --before-gen 0 --after-gen 1 --semantic astro-playroom --format json --out "$env:TEMP/astro-observed.json"
```

The compare command returns 1 when changes are present. It uses the shared
`PluginViewDecoder`, including bounded production F8 collection traversal; there is no
plugin-native decoder hook. The committed fixture and
`AstroDifferentialEvidenceTests` verify all 94 original primitive transitions,
27,999 changed byte positions, zero added/removed semantic values, zero semantic
errors/truncation and collapsed identical backups. The current view decoder reports
107 semantic changes: 94 original primitive transitions plus 13 derived catalogue
ownership-status changes. These extra view fields do not add save primitives.

| Group | Changed primitive values |
| --- | ---: |
| Common | 2 |
| Progress | 10 |
| World | 26 |
| Artifact | 5 |
| Puzzle | 16 |
| Prop | 1 |
| UdsWorkaround | 12 |
| Prompt | 22 |

`NumCoin` is 0 → 1749; `PlayCount` is 1 → 2. Actual coin gain/spend events, limits
and the event incrementing PlayCount are not known.

Artifact ownership transitions: `ds2`, `ps3`, `ps4`. `ps3`/`ps4` cinematic flags
also become true, while `ds2/IsCinematicsPlayed` remains false.

Puzzle ownership transitions: `ssd_glider_2`, `ssd_space_3`, `gpu_forest_1`,
`gpu_forest_3`, `gpu_climbing1_2`, `gpu_highplatforming_3`, `cooling_ice_1`,
`cooling_ice_2`, `cooling_beach_4`. The two SSD records' cinematic flags stay false;
the seven other cinematic flags become true.

Prop ownership transition: `hula_hoop`. Props have no cinematic field in these
samples; the fixture explicitly checks its absence rather than inventing a value.

Several UDS counters change alongside collection/progression flags, but these
mixed-event saves cannot prove cause, direction or the minimal safe batch.
The observations do not prove gameplay-safe dependencies. The 2026-10-28 owner decision
**"Enable writes. Keep warnings."** supersedes the previous evidence gate: F6 collect-all/selected
actions now set existing missing `IsGot` booleans only, with no guessed derived writes. Cinematic,
notification, Progress/World and UDS fields are preserved. Plans and outcomes warn that console
acceptance and integrity are unverified and the game may reject an edited save. Missing or
ambiguous/nonboolean ownership structures are skipped instead of being inserted or rewritten.

`AstroCollectWriteTests` applies all 164 missing flags in generation 0 and 151 in generation 1
across both primary/backup twins. An independent literal-span replacement oracle plus F1 byte
and semantic diffs proves that only ownership text and required displacement/zero fill change;
reopen and one-step Core undo/redo also pass. These are serializer checks, not gameplay evidence.

## Reference mappings and unresolved locations

The F2 pack supplies 50 Inferred candidate artifact names and broad acquisition
worlds; these joins use readable saved IDs, not controlled pickups. `ds3` is
explicitly ambiguous between wiki DualShock 3 and guide SIXAXIS naming. Check all
50 labels against the owner's catalogue before calling them verified.

All 96 puzzle aliases now have an **Inferred candidate internal-stage** mapping via their saved
prefix and a schema-qualified `stageKey`; their physical location and ordinal remain Unknown.
Known non-bijective groups:

| Raw groups | Saved counts |
| --- | --- |
| `gpu_forest` / `gpu_climbing1` | 5 / 3 |
| `gpu_highplatforming` / `gpu_climbing2` | 5 / 3 |
| `cooling_beach` / `cooling_spring1` | 5 / 3 |
| `memory_weather` / `memory_ball1` | 5 / 3 |

These are not verified physical-stage inventories. Public guides' four-piece stage
counts do not justify moving the fifth record into the following prefix group. Candidate-stage
labels are useful for grouping and completion summaries only.
All 18 prop aliases are original readable raw-key labels with Unknown identity,
not verified gatcha scenes. The UI reports their unresolved mapping counts.

Source/licence/provenance are separate from saved-ID confidence: approved CC BY-SA
reuse allows labels to ship, but does not verify a join. See README Sources and
attribution, THIRD-PARTY-NOTICES.md and the pack's `data.source.md`. Live revision
fetches were blocked; exact selected data is pinned by pack version/file hashes.

## Acceptance still requiring owner evidence

Completion round adds F9 original list-order level positions and coordinate-free collectible maps.
Positions have no physical/geographical meaning; candidate world joins remain Inferred, and markers
report the exact saved flag only. The world ownership preset changes only existing mapped IsGot
fields, without deriving cinematic/progression/reward state. Both primary/backup generations pass
an independent literal-span byte oracle for the 25 Memory Meadow records; F8 query exclusions and
one-step undo/redo are verified through Core. These are software guarantees, not new gameplay proof.
The gatcha-budget preset independently uses the externally sourced factual 100-coin cost and
restricts attempts to 0–17. The requested budget is at most 1700, within the observed 0–1749
range; an existing higher balance is preserved, so the final target may exceed that range.
All four records pass exact coin-only byte/reopen tests; actual cost/cap/persistence remain unverified.
F12 uses the shared view decoder and bounded F8 traversal; F13 exposes the read-only
`collectibles/inspect` action and browse-only reference picker. The F4/F6 planned-reference
binding fix (`63580dc`) is adopted through the candidate-world `ReferenceSpec` with mapped-world
eligibility; no enum fallback or outstanding plugin-specific SDK request remains.

The README's exact capture checklist covers control/coin gain/spend pairs, named
artifact/prop/puzzle pickups including all four ambiguous boundaries, stage/boss
clears, catalogue screenshots, update/special-bot transitions and cross-title
transfer pairs. Every enabled write still needs spare-save load, intended
effect, ordinary gameplay, resave/re-dump and a clean second load on PS5 before its
warnings can be narrowed. Validation is tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31), independently of write enablement.
