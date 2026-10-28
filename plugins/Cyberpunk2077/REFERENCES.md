# Reference catalog contract and reproduction

The embedded `cyberpunk2077.identifiers` **1.0.0** F2 pack contains 5,248 item,
19,357 fact, 105 vehicle and 232 travel identifiers. It supplies no localized names,
descriptions, icons or gameplay limits. Native item keys are **unsigned invariant
decimal-string TweakDBIDs**, preserving all 64 bits. Fact keys are unsigned
decimal-string FNV-1a/32 hashes. No string trimming, truncated hashes or fuzzy joins.

Every document resolves `items`, `facts`, `vehicles`, `travel` through
`IReferenceContext.Catalog(id, gameBuild)`; build is the native integer string,
e.g. **`2210`**, not `2.21`. Exact-build local packs can therefore override the
build-agnostic labels. The context stays pinned until reopen. Missing or stale
packs retain the 36-entry baseline item dictionary and raw fact IDs. Category,
limit, localization or image metadata never affects write permission or writer caps.

Optional lawful user packs use the shared F2 manifest and importer, outside this
plugin. For `items` use `id`, `name`, `category`, `description`, optional `assetId`,
and `attributes.internalName` for the exact TweakDB record string. Use locale/build
and full per-dataset provenance, including source-resource hashes and DLC coverage.
The instance details show names/descriptions/images with record and asset attribution.
For `facts`, preserve the exact hash input in `attributes.canonicalName`; a mismatch
falls back to raw hashes. Reference-pack loading rejects duplicate keys; ambiguous
names must be resolved by the pack author, never silently selected by the plugin.

F4 pickers in the Reference catalogs tab inspect records without changing the save.
Items never receive a replacement setter: a base-ID swap is not validated item creation.
Vehicle/travel catalog entries make no saved-state/unlock claim. The current Core
requires `CanEdit` even for read-only actions, so unsupported/read-only documents
show catalog provenance without publishing these inspection actions.

## Rebuild the reviewed embedded pack

Download only these files at revision
`c1fd00305e7db905a4e7de8cfa360d4f4efee3b2` from Deweh/CyberCAT-SimpleGUI
to a scratch folder under `%TEMP%`: `LICENSE`, `items.bin`, and the four
`CP2077SaveEditor/Resources/{ItemClasses,Facts,Vehicles,FastTravel}.json` files.
Retain their original basenames. The source ledger pins each input SHA-256.
Inspect their licence/content before regenerating; do not execute downloaded code.

```powershell
python -I plugins/Cyberpunk2077/reference-data/generate.py "$env:TEMP/cyberpunk13-research"
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
```

`generate.py` independently scans the measured gzip/string dictionary, selects
exact CRC32/UTF-8-length names matching upstream item class IDs, retains the reviewed
36 baseline records, verifies all fact hashes, rejects collisions/duplicate keys,
and emits deterministic LF JSON plus SHA-256 F2 manifests. It does not read saves,
download anything, adapt GPL code or expand the quantity-write allowlist.
Keep both dataset sidecars and third-party notices when updating the pack.

Full TweakDB/localization joins and art redistribution remain owner/rights work
(CP2077-R2/R8/R18). The UI supports separately attributed local text/images without
claiming that source availability licenses redistribution.
