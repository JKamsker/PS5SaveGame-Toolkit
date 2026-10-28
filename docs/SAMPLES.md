# Sample saves

Decrypted PS5 dumps live in `artifacts/` (git-ignored, never commit them). Layout inside each zip:
`<TITLE>/sdimg_<dir>/...` plus the console backup twin `sdimg_sce_bu_<dir>/...`. Many zips contain the
same paths several times (snapshots appended); the importer exposes them as generations 0, 1, ...
`sce_sys/param.sfo` is sometimes a regenerated placeholder (MAINTITLE = title id).

Identities were confirmed by the reconnaissance pass; per-title evidence, format notes and a ranked
roadmap live in [docs/formats/](formats/README.md). Entries marked `?` are still unconfirmed.

| Title | Game | Main files | Notes |
| --- | --- | --- | --- |
| PPSA01288 | Sackboy: A Big Adventure | `SaveGame/General`, `LevelImages_*` | |
| PPSA01325 | ASTRO's PLAYROOM | `sce_sdmemory/memory.dat` (1 MiB) | save-data memory API blob |
| PPSA01463 | Borderlands 3 | `profile`, `1` | |
| PPSA01474 | Ratchet & Clank: Rift Apart | `i29-s.save`, `i29-userprefs.save` | |
| PPSA01493 | Tiny Tina's Wonderlands | `profile`, `1` | same layout as PPSA01463 |
| PPSA01494 | Borderlands 4 | `gbxsavegame.sav` | |
| PPSA01506 | Immortals Fenyx Rising | `AutoSave0x.sav`, `Options.sav` | |
| PPSA01603 | Hogwarts Legacy | `HL00xx/ue4savegame.ps5.sav` | Unreal |
| PPSA01651 | YouTube (app) | `STARBOARD/*`, `memory.dat` | not a game |
| PPSA01802 | Kena: Bridge of Spirits (from icon0.png key art) | `ue4savegame.dpx.sav` | Unreal |
| PPSA01876 | Far Cry 6 | `profile.dat` | |
| PPSA01911 | Final Fantasy VII Remake Intergrade | `ff7remake0xx` (9 MiB each) | |
| PPSA01982 | DOOM Eternal | `PROF` | |
| PPSA02199 | STAR WARS Jedi: Fallen Order | `ue4savegame.dpx.sav` | Unreal |
| PPSA02225 | Ghost of Tsushima | `suckerpunchUserN/dis.dat`, `memory.dat` | |
| PPSA02343 | ? (JSON profile) | `ProfileData/ue4savegame.dpx.sav` | Unreal |
| PPSA02530 | Pragmata | `data0xx*.bin` | RE Engine DSSS/Mandarin; analysis in `PRAGMATA_PS5_Save_Editor_Toolkit.zip` |
| PPSA02745 | Tales of Arise | `SaveData_00x.sav`, `SystemSaveData.sav` | |
| PPSA03169 | Elden Ring (JP) | `memory.dat` (29 MiB) | |
| PPSA04018 | The Witcher 3 (Complete Edition) | `CheckPt.*.sav`, `Manual.*.sav`, `*.req` | |
| PPSA04027 | Cyberpunk 2077 | `sav.dat`, `metadata.9.json`, `settings.json`, `user.gls` | |
| PPSA06251 | Star Ocean: The Divine Force | `SAVEDATA_000xx.DAT`, `SYSDATA` | |
| PPSA07696 | Ridge Racer 2 (PSP classic, UCES00422) | `DATA.BIN`, `imgui_emu_option.json` | emulated PSP save |
| PPSA07821 | Overwatch 2 (settings) | `settings/data` | |
| PPSA08330 | God of War Ragnarök | `GOWRSAVEn` | |
| PPSA08664 | Final Fantasy VII Rebirth | `ff7rebirth0xx` (23 MiB) | |
| PPSA10665 | Final Fantasy XVI | `FAITHDATAnnnn/game-save-data` | same family as PPSA13803 |
| PPSA10695 | Atomic Heart (custom SaveExtension, not GVAS) | `ue4savegame.dpx.sav` | Unreal |
| PPSA13803 | Final Fantasy XVI (demo) | `FAITHDATA*/game-save-data*` | |
| PPSA16064 | ? (JSON + XOR-1 settings) | `Character*/ue4savegame.dpx.sav`, `GlobalSaveDataDefault` | Unreal |
| PPSA18463 | Baldur's Gate 3 | `Save0001/AutoSave_0.lsv`, `Profile/*.lsf/.lsx` | Larian LSPK/LSF |
| PPSA18936 | Final Fantasy VII Rebirth (demo) | `ff7rebirth00x` | |
| PPSA26344 | Ghost of Yōtei | `suckerpunchAutoN/dis.dat` | same family as PPSA02225 |

## Capture protocol (controlled before/after pairs)

The consolidated, prioritised owner queue is [Owner capture and console-validation guide](OWNER_CHECKLIST.md).
Use it for the title-specific pairs and console checks below.

A single snapshot can't show which bytes hold a quantity, an ownership flag or a limit. Evidence
comes from **pairs**: the same save taken right before and right after **one** known in-game event.

1. **Pick the smallest event that pins down one field.** Buy one item, sell one item, gain exactly
   one level, equip one piece, pick up one collectible, spend one skill point. Avoid events with
   side effects (cutscenes, autosave-triggering area changes, quest steps) unless that's the subject.
2. **Prepare.** Note the game version (*Settings → Information* on the console), region, installed
   DLC and the save slot/character. If possible, turn off cloud sync for the title so the console
   doesn't swap in another copy.
3. **Capture a control pair first** (once per title and build): save, load that save, save again
   in the same spot without doing anything, and export both. Whatever differs here is **churn**:
   clocks, counters, play time, relocations. It goes into the title's `ignore` rules with a reason.
4. **Capture the event pair.** Save (or wait for the autosave), export (*before*), do exactly one
   action, save in the same slot, export again (*after*). Don't play in between.
5. **Export** with your save manager as a decrypted dump (`<TITLE>/sdimg_<dir>/...`) and name the
   archives `<TITLE>_<event>_before.zip` / `<TITLE>_<event>_after.zip`, for example
   `PPSA01325_collect-one-coin_after.zip`. Keep them in `artifacts/` (never commit them). If the
   manager appends snapshots to one archive, keep it: each repeat becomes a generation (see below).
6. **Record what you did** as a fixture in a manifest (format below): title, build, platform,
   region, slot, DLC, event, archive names (+ SHA-256), and the values the game showed before and
   after (e.g. "Potions: 3 → 4", "Gold: 1,250 → 1,200"). Values the game shows are the ground truth
   the diff has to explain.
7. **Check it** with `savediff compare` and turn the confirmed changes into `expected` entries, so
   `savediff verify` and plugin tests can re-check them later.

What to record per pair:

| Field | Example | Why |
| --- | --- | --- |
| title / game | `PPSA01325`, ASTRO's PLAYROOM | which plugin and samples |
| build, platform, region | `01.004.000`, PS5, EU | layouts change between patches |
| slot / character | slot 1, "Tav" | which file and record changed |
| DLC / mods | none | extra records and IDs |
| event | "Bought 1 Potion for 50 gold" | the single cause |
| shown values | potions 3 → 4, gold 1250 → 1200 | expected values |
| control | yes/no | churn learning vs. evidence |
| captured on | 2026-10-28 | provenance |

Never put identity or credential values (account IDs, UIDs, tokens, PSN names) into a manifest,
an issue or a committed report. Diff output can contain them, so share it only after redacting.

## Differential-save tool (`savediff`)

`tools/SaveToolkit.SaveDiff` is a read-only developer tool. The engine lives in
`src/SaveToolkit.Testing/Diff`, so plugin test suites can call `SaveSetDiffer.Compare(...)` and
`FixtureVerifier.Verify(...)` on `SampleSaves.Load(...)` sets directly.

```powershell
$diff = 'tools/SaveToolkit.SaveDiff'
dotnet run --project $diff -- list artifacts/PPSA01325_saves_dec.zip
dotnet run --project $diff -- compare before.zip after.zip --semantic auto
dotnet run --project $diff -- list-decoders
dotnet run --project $diff -- compare before.zip after.zip --decoder <listed-hook-id>
dotnet run --project $diff -- compare artifacts/PPSA01325_saves_dec.zip artifacts/PPSA01325_saves_dec.zip `
    --before-gen 0 --after-gen 1 --semantic astro-playroom --ignore plugins/X/fixtures.json
dotnet run --project $diff -- verify plugins/X/fixtures.json --format json
```

How it works:

- **Inputs:** a decrypted dump `.zip`, a folder or a single file. A path that occurs several times
  in one archive becomes generations 0, 1, … in archive order (as in the app).
  - `--before-gen` / `--after-gen` take `all` (default, pairs gN with gN), `latest` or a number.
  - Use the same zip on both sides to compare two snapshots of one dump.
- **Pairing:** files pair by title, logical save directory, backup flag and path, never by archive
  position.
  - Each main file is checked against its `sdimg_sce_bu_` twin. The report lists twins that
    differ or are missing.
  - A backup that equals its main file on both sides is shown as `MirrorsMain` instead of being
    diffed twice (`--no-collapse` turns this off).
  - `sce_sys/*` is skipped unless you pass `--system`.
- **Byte mode** (no plugin needed): changed ranges with hex context (changed bytes in `[...]`),
  growth and shrinkage, and per-file counts. The number of reported ranges is capped
  (`--max-ranges`), but every byte is counted.
- **Semantic mode** (`--semantic json|auto|<plugin-id>`): a decoder turns each file into typed
  values with identity paths. Records align by identity, not offset, and the report lists added,
  removed and changed paths. `--decoder <id>` also selects an explicit plugin hook listed by
  `list-decoders` (exclusive with `--semantic`). Decoders implement `ISaveDecoder` or the SDK's
  `ISaveDecoderHook`, exposed by a catalogued plugin's optional `ISaveDecoderProvider`:
  - `JsonSaveDecoder` aligns arrays of objects by `id`-like keys, e.g. `items[id=42]/qty`.
  - `PluginViewDecoder` turns any existing plugin into a decoder by using its `FieldNode` value
    ids as paths. It expands lazy sections and pages through F8 queryable tables, including direct
    read-only text and rich cell titles under stable collection/row/column paths. Default query order
    must be deterministic; offset/cursor positions never become row identities.
  - `auto` uses the best-probing plugin, then JSON.
- **Bounds:** inputs are copied to a private temp folder, which is deleted afterwards, and byte
  diffs stream in 64 KiB blocks. Decoded trees, value lengths, reported ranges and reported
  changes are all capped; the report says when something was cut. Across all F8 collections in a file,
  defaults are 100,000 rows, 1,000 pages and 32 MiB of complete UTF-8 response pages. Override them with
  `--max-query-rows`, `--max-query-pages`, `--max-query-bytes` (zero disables traversal for that budget).
  Over-budget pages are omitted. `inputTruncated` and warnings identify incomplete semantic inputs;
  absent paths then do not establish removal. See [the F1 plugin guide](PLUGIN_GUIDE.md#differential-save-tooling-f1--f12)
  for native hooks and identity/path rules.
- **Output:** text, or `--format json` (`savetoolkit-savediff/1`). `--out` refuses paths inside
  `artifacts/` or any git working tree.
  `list-decoders` defaults to ID/kind/plugin columns; its JSON envelope is `savetoolkit-decoders/1`.
  CLI options override fixed defaults; decoder selection has no environment/config fallback.
  Commands never prompt or read stdin. Reports go to stdout (or checked `--out`); usage/input errors
  go to stderr with exit code 2 in both output modes. Inputs are read-only; there is no destructive
  operation or confirmation flow. `SAVETOOLKIT_ARTIFACTS` only locates sample/fixture archives, with
  `verify --artifacts` taking precedence.
- **Exit codes:**
  - `0`: no changes, or all fixtures passed.
  - `1`: changes found, or a fixture failed.
  - `2`: usage or input error.

  `verify` reports fixtures whose archives are missing as *skipped*.

## Fixture manifest (`savetoolkit-fixtures/1`)

One JSON format for all test suites. A manifest holds no save bytes. It names archives relative
to the artifacts folder, so it may be committed, for example as `plugins/<Name>/fixtures.json`.
A manifest that has only an `ignore` block is the per-title churn list that `--ignore` takes.
Comments and trailing commas are allowed, but unknown properties are rejected.

```jsonc
{
  "format": "savetoolkit-fixtures/1",
  "description": "ASTRO's PLAYROOM evidence pairs",
  "ignore": [
    // Masks only the given bytes / paths / files. Masked differences are still counted in the report.
    { "titleId": "PPSA01325", "file": "sce_sys/param.sfo", "reason": "console metadata, rewritten on every save" },
    { "titleId": "PPSA01325", "file": "memory.dat", "offset": 1056, "length": 8, "reason": "save timestamp (control pair 2026-10-28)" },
    { "titleId": "PPSA01325", "path": "*/PlayTime*", "reason": "play-time counter" }
  ],
  "fixtures": [
    {
      "id": "ppsa01325-collect-one-coin",
      "titleId": "PPSA01325",
      "game": "ASTRO's PLAYROOM",
      "build": "01.004.000",
      "platform": "PS5",
      "region": "EU",
      "slot": "default",
      "dlc": [],
      "event": "Collected one coin in CPU Plaza",
      "control": false,
      "capturedOn": "2026-10-28",
      "notes": "Coin counter showed 1749 → 1750.",
      "before": { "archive": "PPSA01325_collect-one-coin_before.zip", "generation": "latest", "sha256": "…" },
      "after":  { "archive": "PPSA01325_collect-one-coin_after.zip",  "generation": "latest", "sha256": "…" },
      "decoder": "astro-playroom",
      "files": ["memory.dat"],
      "expected": [
        { "file": "memory.dat", "path": "/PreInstall[0]/Common[0]/NumCoin[0]/text[0]", "before": "1749", "after": "1750" },
        { "file": "memory.dat", "offset": 0, "length": 16, "changed": false, "note": "zero header untouched" }
      ],
      "strict": true
    }
  ]
}
```

The values above are illustrative; the PPSA01325 pair does not exist yet.

| Property | Meaning |
| --- | --- |
| `ignore[]` (manifest or fixture) | Churn rules. `titleId` (omit or `*` for any), `directory` and `file` globs (`*`, `?`; directory = logical name without `sdimg_`). Then `offset` + `length` (bytes), or `path` (semantic glob), or neither (whole file). `reason` is required. |
| `before` / `after` | `archive` (relative to `--artifacts`, `SAVETOOLKIT_ARTIFACTS` or `./artifacts`), `generation` (`all`, `latest` or a number), optional `sha256` of the archive. |
| `decoder` | `json`, `auto`, a plugin id or a registered hook id from `list-decoders`. Omit it for byte expectations only. |
| `files` | Globs limiting which files are compared. |
| `expected[]` | One change per entry, in main files (`"backup": true` for backups) matched by `directory`/`file`. It is either a byte range (`offset`, `length` ≤ 4096, optional hex `before`/`after`) or a semantic `path` (optional value text; `(absent)` = path missing). `"changed": false` asserts that nothing changed there. Every matched file must satisfy it. |
| `strict` | Every unmasked change must be covered: each changed file by some expectation, each byte range by a byte expectation, each semantic change by a path expectation. |
