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
$taskScratch = 'D:/Temp/saveeditor/test-scratch/docs/accuracy-talesofarise'
New-Item -ItemType Directory -Force -Path $taskScratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = $taskScratch
$env:TEMP = $taskScratch
$env:TMP = $taskScratch
$scratch = Join-Path $taskScratch 'catalog-sources'
python -I -X utf8 plugins/TalesOfArise/ReferenceData/fetch_catalog.py --sources "$pack/source-revisions.json" --cache $scratch
python -I -X utf8 plugins/TalesOfArise/ReferenceData/test_wiki_tables.py
python -I -X utf8 plugins/TalesOfArise/ReferenceData/build_catalog.py --cache $scratch --sources "$pack/source-revisions.json" --output $pack
python -I -X utf8 plugins/TalesOfArise/ReferenceData/build_assets.py --scratch "$taskScratch/publisher-assets"
dotnet test --project plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests -c Release
```

The base builder emits the text-only v2 manifest; the overlay must then restore the
v3 assets, descriptions, mappings and manifest before testing or comparing outputs.
Run this complete sequence again and require no data diff for a reproducible
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

## Offline regeneration checks

Run all Python checks before and after importer changes, with the external scratch
environment above (no network access or game samples are needed):

```powershell
python -I -X utf8 -B -m unittest discover -s plugins/TalesOfArise/ReferenceData -p 'test_*.py' -v
```

Dependencies are pinned in `requirements.txt`. Goldens were captured **before** the
importer refactor for #40 on 2026-10-29, using Python 3.14.3, Beautiful Soup 4.15.0,
Pillow 12.2.0, libwebp 1.6.0 and Pillow's zlib 1.3.1.zlib-ng. Encoder versions matter;
see Pillow's [WebP/PNG encoder options](https://pillow.readthedocs.io/en/stable/handbook/image-file-formats.html).
Install dependencies separately; tests never install or fetch anything.

- `importer_fixtures.py` constructs 20 small, authored API responses with source
  title/revision/hash pins. `test_catalog_golden.py` runs the real hash validation,
  parser, extraction, reconciliation and serialization twice, comparing all eight
  data files, `pack.json` and `source-revisions.json` against `golden/catalog/` as
  bytes. The fixtures cover every extraction category, duplicate variants, title
  nodes, repeated character roles, inferred crosswalks, Unicode and attribution.
  Rejection checks cover hashes, identity, omitted names, panel shape and collisions.
- `test_assets_golden.py` uses the **committed v3 pack as its golden**, removes the
  overlay from a scratch copy, and reapplies it twice. Only the download/encoder
  boundary is replayed with reviewed encoded bytes/dimensions from the ledger.
  The real pipeline validates encoded pins, budgets/copies assets, joins records
  and descriptions, and refreshes hashes/attribution. All **124 files** must match:
  eight data files, manifest, source-revision and asset ledgers, both Markdown
  attribution sidecars and all 111 encoded images. Markdown sidecars are maintained
  by hand; this checks preservation, not their generation. Budget/pin rejections
  must leave the entire destination unchanged.
- `test_image_encoder.py` independently runs the actual encoder on generated pixels,
  pinning bytes via SHA-256, lengths, dimensions and selected format for lossless
  WebP, PNG-8 and a resized lossy map. Unsupported/corrupt images are rejected.
  These vectors characterize encoding; they do not reconstruct publisher originals.
- Expected checked-out JSON is canonicalized to the LF bytes used by importer
  hashes (Git `autocrlf` can expand it). Generated output is **never** normalized
  before comparison. Fixture creation is not part of the test run; do not recapture
  goldens merely to make a failing change pass.

### Regeneration limits

The 62 original rendered API responses and 111 original publisher-image downloads
were **not available offline** in the available `D:/Temp` scratch during this review.
They remain external inputs pinned by `source-revisions.json` (`cache`, `sha256`,
revision and raw-wikitext hash) and `asset-sources.json` (`sourceUrl`,
`originalSha256`, encoded `sha256`, dimensions). Original images and downloaded
responses are deliberately not committed. The two crosswalk URLs/hashes in
`build_catalog.py` are provenance for authored constants, not files read by the
importer. Official description transcriptions and mappings are reviewed ledger
inputs, not newly scraped by `build_assets.py`.

Consequently these tests do **not** claim end-to-end regeneration of the shipped
catalog from all original wiki responses, or re-encoding of its original artwork.
They cover the deterministic stages available locally, without skipping tests or
using the network. Full original-source regeneration still requires the external
hash-matching caches and the pinned encoder stack, followed by the complete base
then overlay commands above. Tests reject network attempts even on cache misses.
The three previously deferred long importer functions can be split behind this
coverage; this resolves their cleanup, while original-input recovery remains an
explicit evidence gap. Runtime code and shipped reference-pack bytes are unchanged.

All adapted source data stays [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/),
credited to Aselia Wiki contributors. Every dataset, record, adjacent sidecar,
plugin source list and rendered view must retain attribution. New source pages need
the same author, licence link, retrieval date, modifications and immutable revision.

## Publisher-media overlay (v3)

After regenerating the base catalogs, reapply the reviewed overlay:

```powershell
python -I -X utf8 plugins/TalesOfArise/ReferenceData/build_assets.py --scratch "$taskScratch/publisher-assets"
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
