# PS5 Save Game Toolkit

[![CI](https://github.com/JKamsker/PS5SaveGame-Toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/JKamsker/PS5SaveGame-Toolkit/actions/workflows/ci.yml)

A local, web-based editor for **decrypted** PS5 save games. Drop a save dump (zip or folder),
the toolkit detects the game, and a game plugin describes the data as an abstract view that the
browser renders: stats, typed fields, tables, actions. Edits are validated and can be undone within
the [history limits](docs/PLUGIN_GUIDE.md#structural-edits-add--remove--reorder). Export verifies output
by re-opening it and preserves the folder layout, with optional backup mirroring where the twin held
the same original bytes and ownership permits it. Explicit document groups manage their own backups.

The toolkit does not decrypt or re-sign PS5 save containers; use your save manager for that.
**Edits are not console-validated. All game plugins are Experimental; the game may reject,
normalize or overwrite an edit even when format checks pass.** Keep an untouched backup and test
on a disposable spare slot. The owner validation work is tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31) and the
[owner checklist](docs/OWNER_CHECKLIST.md).

## Features

- **F1 — Save comparison:** read-only byte/semantic diffs and repeatable evidence fixtures.
- **F2 — Reference packs:** offline names, descriptions and permitted images with attribution.
- **F3 — Rich tables:** live search, filters, row selection, images, confidence and expandable details.
- **F4 — Reference pickers:** searchable raw-ID choices with eligibility checks and source details.
- **F5 — Structural undo:** reversible add/remove/reorder operations where a plugin implements them.
- **F6 — Review before apply:** bulk-action previews, skipped rows, one-step undo within history limits and a Changes panel.
- **F7 — Related files:** explicit document groups with coordinated edits, history and export.
- **F8 — Large collections:** bounded server-side search/filter/sort and selection across unloaded pages.
- **F9 — Maps and graphs:** read-only diagrams with text lists and external map links.
- **F10 — Exact values:** JSON numbers without rounding, explicit nulls and typed alternatives.
- **F11 — Shared codecs:** managed Kraken decoding/stored-block writing and in-memory SQLite images.
- **F12 — Decoder hooks:** SaveDiff traverses paged views and discovers explicit plugin evidence decoders.
- **F13 — Read-only tools:** isolated inspection actions, browse-only pickers, catalogue sorting and float durations.

These are framework capabilities; each plugin adopts only the parts its format supports. A reference
name or graph does not establish ownership, completion, prerequisites or a safe gameplay edit.

## Supported games and formats

There are **17 game/format plugins**, plus a built-in raw fallback viewer. Each linked README gives
the exact titles, versions, bounds, confidence and unsupported operations. Support for a file does
not imply support for every game build or region.

| Plugin / titles | Maturity | View | Edit (within supported schemas) |
| --- | --- | --- | --- |
| [AstroPlayroom](plugins/AstroPlayroom/README.md) — ASTRO's PLAYROOM | Experimental | Coins, saved collectibles, progress/world flags, candidate aliases, maps and raw tree | Existing coins/play count and supported booleans; reviewed collectible/world and gatcha-budget presets |
| [AtomicHeart](plugins/AtomicHeart/README.md) — Atomic Heart | Experimental | SaveExtension records, populated inventory, asset references and level/skill evidence | Existing Amounts, constrained same-category asset replacements and reviewed Amount batches |
| [BaldursGate3](plugins/BaldursGate3/README.md) — Baldur's Gate 3 | Experimental | Package/resources, metadata, all-owner inventory candidates, live NewAge storage, classes/spell occurrences | Supported v18/LSF primitives and summary tokens; bounded NewAge quantities, HP/ability-cache candidates, two-key container reorder and imported-quantity restore. Source rows are not authoritative party identities |
| [Cyberpunk2077](plugins/Cyberpunk2077/README.md) — Cyberpunk 2077 | Experimental | Player/inventory, equipment/cyberware, perks, facts, garage/travel and journal diagnostics | Money, attributes/point balances, supported stacks, raw Level/Cred/XP/thresholds and existing facts; reviewed stack targets/refills |
| [EldenRing](plugins/EldenRing/README.md) — Elden Ring | Experimental | Character, inventory/storage, equipment, regulation and raw progress candidates | Held runes, mirrored names/history, two allowlisted positive stack types and reviewed rune/refill actions |
| [FinalFantasy16](plugins/FinalFantasy16/README.md) — FF XVI retail/demo | Experimental | Inventories/banks, equipment, raw ability arrays, player/party context and reference graphs | Supported existing IDs/stacks, gear, raw ranks/flags, player/party context and time; reviewed main-stack targets |
| [FinalFantasy7](plugins/FinalFantasy7/README.md) — FF VII Remake/INTERmission, Rebirth retail/demo | Experimental | Characters, items, materia, gear, mapped progress candidates and reference/world views | Gil, mapped counts, HP/MP/XP/level, ordinary materia AP/stars, qualified Rebirth owned-weapon references and individual progress bits; restock/star presets |
| [Gearbox](plugins/Gearbox/README.md) — Borderlands 3 / Tiny Tina's Wonderlands | Experimental | Character/profile, named inventory/parts, missions, skill references and account mirrors | Progression, currencies/keys/ammo, supported serial parts/levels/marks, SDUs/difficulty, import/duplicate/remove; F7 bank/lost-loot and existing account fields. BL4 is research-only and excluded |
| [GenericJson](plugins/GenericJson/README.md) — JSON; Cyberpunk settings, It Takes Two profile, Ridge Racer 2 wrapper | Experimental | Exact JSON tree, title overlays, recorded choices/defaults and present profile keys | Generic scalar/structural JSON; reviewed existing settings, value/index pairs, present minigame/chapter/checkpoint values and wrapper resets |
| [GodOfWarRagnarok](plugins/GodOfWarRagnarok/README.md) — God of War Ragnarök | Experimental | Wallet/resource/XP/raven counters, gear/skill candidates, flags, references and integrity diagnostics | Existing decoded wallet counters, play time and reviewed restore-to-imported counters; preferences remain read-only |
| [Insomniac](plugins/Insomniac/README.md) — Ratchet & Clank: Rift Apart | Experimental | Weapons/armour/upgrades, collectibles/world, preferences and raw DDL | CareerBolts/SpentBolts, seven existing gold-bolt options, 61 scalar boolean preferences and reviewed calculated bolt target |
| [Pragmata](plugins/Pragmata/README.md) — Pragmata | Experimental | Inventory, gadgets/loadout, progress/quests/world, mission references and raw tree | Existing IDs/quantities, header/clocks and supported primitives in five family tabs; reviewed quantities. RSA trailer remains unverified |
| [SuckerPunch](plugins/SuckerPunch/README.md) — Ghost of Tsushima / Ghost of Yōtei | Experimental | Tsushima resources/ammo and equipment/technique/world candidates; Yōtei inventory, statistics and gear-registry diagnostics | Tsushima existing resources/ammo, supported v48/v49 timer and reviewed quantities; Yōtei 14 eligible existing quantity types and reviewed targets, with unknown checksum tail coverage |
| [TalesOfArise](plugins/TalesOfArise/README.md) — Tales of Arise EU base game | Experimental | Sections, characters, inventory/equipment, skill/arte and progress evidence | Gald, existing active-character level/HP/SP, recognized consumable quantities and reviewed quantity batches |
| [UnrealGvas](plugins/UnrealGvas/README.md) — GVAS; Hogwarts Legacy, Jedi: Fallen Order, Sackboy | Experimental | Nested properties/actors, schema references and Hogwarts database rows | Supported existing GVAS scalars, including warned Jedi/Sackboy fields; bounded Hogwarts nonidentity integer/real/text cells in unwrapped, fully decoded image pairs. Known checksum fields and Jedi actor-stream lengths stay read-only |
| [Witcher3](plugins/Witcher3/README.md) — The Witcher 3 Complete Edition | Experimental | Player/inventory, skills/mutations, Gwent, facts/journal and map-tag evidence | Level/XP/free points/Crowns, quantities/durability, existing non-core ranks/Gwent copies; reviewed repair and food/water refill |
| [XorJson](plugins/XorJson/README.md) — obfuscated JSON / Minecraft Dungeons II offline schema | Experimental | Exact settings/character tree, inventory, attributes, quests/world and reference graph | Scalar/structural JSON, reviewed existing-value batches and F7 character/global group edits; unsupported character versions/online modes stay read-only |

## Using the toolkit

Download the bundle for your OS from [GitHub Releases](https://github.com/JKamsker/PS5SaveGame-Toolkit/releases)
(repository access is required while this project is private). Verify its SHA-256 checksum, extract
the whole archive into a writable folder, and run `start.cmd` on Windows or `./start.sh` on macOS/Linux.
On macOS/Linux, run `chmod +x start.sh SaveToolkit.Api` first if needed. Open
http://127.0.0.1:5180. No .NET or Node.js installation is needed.
The [user guide](docs/USER_GUIDE.md) covers checksum commands, other ports, importing,
editing, review/undo, export, supported titles and troubleshooting. Keep backups;
edits are experimental and not console-validated. Maintainers: see
[release instructions](docs/RELEASING.md) for packaging and publishing.

### Run from source

Install the .NET 10 SDK selected by [global.json](global.json), Node.js 24 and npm.
From the repository root:

```powershell
cd web; npm ci; npm run build; cd ..          # builds the UI into src/SaveToolkit.Api/wwwroot
dotnet run --project src/SaveToolkit.Api      # http://127.0.0.1:5180
```

For development, run the API above and `cd web; npm run dev` in a second terminal
(http://localhost:5173, proxies `/api` to port 5180).

## Tests and private samples

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS = 'C:/path/to/private/artifacts'
dotnet test --project tests/SaveToolkit.Core.Tests
dotnet test --project plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests
./scripts/test.ps1                           # API build + all backend test projects
# ./scripts/test.ps1 -Filter Pragmata        # optional project-path substring filter
cd web; npm ci; npm test; npm run build
```

Tests use Microsoft.Testing.Platform: pass `--project`. Missing private samples cause tests to
skip; zero failures with skips is not a complete sample-backed run. Set the sample directory in
the same shell and check each project's summary for zero skipped. Keep saves outside the checkout;
never commit decrypted dumps or extracted data. See [samples and SaveDiff](docs/SAMPLES.md) for the
capture protocol, decoder discovery and fixture verification.

Documentation checks use Python 3.10+ with no extra packages:
`python -B docs/tests/test_documentation.py`. They check local links/anchors, commands and selected
source-backed claims against `HEAD`; add `--code-ref main` to audit against local `main`.

## Architecture

- **SDK** (`src/SaveToolkit.Sdk`): the plugin contract. Plugins describe *what* data is — types,
  ranges, labels, grouping, confidence — never *how* it looks.
- **Core**: import, generations and backup pairing, plugin probing, edit validation against the
  plugin's own specs, undo/redo, export with verification.
- **Api**: thin HTTP layer. **Web**: renders the view protocol.
- **Codecs**: bounded managed Kraken decoding/stored writes and native SQLite image operations
  entirely in memory. See [the codec contracts](src/SaveToolkit.Codecs/README.md).
- **Plugins** (`plugins/`): one per game or engine format. See [docs/PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md).

Session plugin calls are serialized. Closing a workspace disposes its documents and rejects later
session/group opens through retained workspace references. Export verifies plugin output by reopening
it; plugins remain responsible for atomic edits, format integrity and byte-identical no-op round trips.

The server binds to 127.0.0.1 and has no authentication; it is meant to run on your own machine.

Licensed under **GPL-3.0-or-later**; see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).
Upstream components retain their own compatible licences and attribution.
The [licence exclusions register](docs/LICENCE_EXCLUSIONS.md) tracks remaining refusals and imports in
progress. Per the owner decision of 2026-10-28, unlicensed community facts/data are reused with full
attribution; unlicensed code is reimplemented with source credit. The 2026-10-29 owner decision also
permits rights-reserved guide material and publisher assets with source, rights-holder and revision/date
attribution. GPL-2.0-only code and NC/no-redistribution terms remain refusal reasons. See
[plugin guide rule 9](docs/PLUGIN_GUIDE.md#hard-rules) for the exact policy and required notices.
