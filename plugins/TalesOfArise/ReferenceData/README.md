# Maintaining the Tales of Arise catalogs

These are developer reference-data import utilities. The plugin never runs them and
does no network or filesystem I/O. All downloads remain in a scratch folder outside
the repository. These scripts contain independently authored parsing code; downloaded
wiki text is data and is never executed. The only Python dependency is
`beautifulsoup4` (tested with 4.15.0).

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
