# PS5 Save Game Toolkit

A local, web-based editor for **decrypted** PS5 save games. Drop a save dump (zip or folder),
the toolkit detects the game, and a game plugin describes the data as an abstract view that the
browser renders: stats, typed fields, tables, actions. Edits are validated, undoable, verified by
re-opening the output, and exported in the same folder layout (including the console backup copy).

The toolkit does not decrypt or re-sign PS5 save containers; use your save manager for that.
Always keep a backup and test edits on a spare slot.

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
- **Plugins** (`plugins/`): one per game or engine format. See [docs/PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md).

The server binds to 127.0.0.1 and has no authentication; it is meant to run on your own machine.
