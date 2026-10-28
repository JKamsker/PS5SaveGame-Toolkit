# PS5 Save Game Toolkit

A local, web-based editor for **decrypted** PS5 save games. Drop a save dump (zip or folder),
the toolkit detects the game, and a game plugin describes the data as an abstract view that the
browser renders: stats, typed fields, tables, actions. Edits are validated, undoable, verified by
re-opening the output, and exported in the same folder layout (including the console backup copy).

The toolkit does not decrypt or re-sign PS5 save containers; use your save manager for that.
Always keep a backup and test edits on a spare slot.

Game support: 18 plugins covering 24 of the 33 sample titles, among them Pragmata, Baldur's Gate 3,
Cyberpunk 2077, The Witcher 3, Elden Ring, FF VII Remake/Rebirth, FF XVI, Borderlands 3, Ghost of Tsushima
and Tales of Arise. See [docs/PLUGINS.md](docs/PLUGINS.md) for what each one can edit.

## Run

```powershell
cd web; npm install; npm run build; cd ..     # builds the UI into src/SaveToolkit.Api/wwwroot
dotnet run --project src/SaveToolkit.Api      # http://127.0.0.1:5180
```

Development: `dotnet run --project src/SaveToolkit.Api` and `cd web; npm run dev` (http://localhost:5173).

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

Run the complete backend suite with decrypted samples available:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/path/to/artifacts'
./scripts/test.ps1
```

The script builds the API and plugins, then runs every framework and plugin test project. Missing
samples cause tests to skip; verify that each summary reports zero skipped for a complete run.

The server binds to 127.0.0.1 and has no authentication; it is meant to run on your own machine.

Licensed under **GPL-3.0-or-later**; see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).
Upstream components retain their own compatible licences and attribution.
