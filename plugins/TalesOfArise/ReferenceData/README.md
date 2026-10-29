# Maintaining the Tales of Arise catalogs

These are developer reference-data import utilities. The plugin never runs them and
does no network or filesystem I/O. All downloads remain in a scratch folder outside
the repository. These scripts contain independently authored parsing code; downloaded
wiki text is data and is never executed. Catalog parsing uses
`beautifulsoup4` (tested with 4.15.0); the asset overlay also uses Pillow 12.2.0.

The source manifest is
`../SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog/source-revisions.json`.
It pins 62 MediaWiki page revisions, exact rendered-API response SHA-256s, raw wikitext
SHA-256s and extraction counts. Fetching a pinned revision can still expand newer
templates; this is why the entire response is checked, rather than trusting `oldid`
alone. A changed response requires reviewed research and a new pack version.

From the repository root, in PowerShell:

```powershell
$pack = 'plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog'
$scratch = Join-Path $env:TEMP 'toa-r2-research'
python -I -X utf8 plugins/TalesOfArise/ReferenceData/fetch_catalog.py --sources "$pack/source-revisions.json" --cache $scratch
python -I -X utf8 plugins/TalesOfArise/ReferenceData/test_wiki_tables.py
python -I -X utf8 plugins/TalesOfArise/ReferenceData/build_catalog.py --cache $scratch --sources "$pack/source-revisions.json" --output $pack
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests
```

Run the fetch/import sequence again and require no data diff for a reproducible
regeneration. The importer independently checks every published named Item/Arte/Skill/
Title template against the extracted labels; commented-out unnamed-animation guesses
are deliberately excluded. Inspect the source-specific table headings, first/last named rows,
rowspan continuations, title-node costs, duplicate labels and unknown values before
changing the manifest/version. All 2,157 records are reference facts; only 13 legacy
consumables have inferred numeric identities. Source order never becomes a save ID.

`build_catalog.py` validates cached response hashes and revision/title identity;
`wiki_tables.py` handles nested navigation, row/column spans and explanatory rows.
Synthetic parser tests cover the important table shapes without embedding downloaded
HTML. The C# suite checks full catalog counts, valid packs, pinned share-alike sources,
title-node joins/costs, unknown identities, paging and sample-byte preservation.

All adapted source data stays [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/),
credited to Aselia Wiki contributors. Every dataset, record, adjacent sidecar,
plugin source list and rendered view must retain attribution. New source pages need
the same author, licence link, retrieval date, modifications and immutable revision.

## Publisher-media overlay (v3)

After regenerating the base catalogs, reapply the reviewed overlay:

```powershell
python -I -X utf8 plugins/TalesOfArise/ReferenceData/build_assets.py --scratch "$env:TEMP/toa-assets"
```

The builder reads the reviewed `asset-sources.json` ledger, downloads pinned image
URLs to scratch, validates original SHA-256s and actual image headers, and encodes
outside the repository. It copies only validated compact outputs into this plugin.
It checks encoded hashes (Pillow 12.2.0), deduplicates identical outputs, enforces
64×64 icons / 2048px map bounds and the 4,000,000-byte total, then adds record links
and the reviewed official localized-description transcriptions and refreshes hashes.
Re-running the overlay must leave every pack byte unchanged. If a source or encoder
changes, review it and deliberately update the pins rather than bypassing checks.
The ledger's exact source/record mappings are reviewed data; automatic fuzzy matching
and guessed save identities are not part of regeneration.

Official descriptions use only the specifically labelled Tales of Arise localized
wording in the linked Aselia revision. The ledger includes each source response hash;
raw HTML/API responses are retained in external scratch, not embedded. Media and text
have separate provenance in `assets.source.md`, with CC BY-SA attribution retained
for wiki text and the owner's 2026-10-29 decision recorded for publisher material.
