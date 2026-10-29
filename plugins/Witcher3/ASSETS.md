# Witcher3 asset round — 2026-10-29

Version 0.7.0, Experimental. Branch: `plugin/witcher3-assets`. Owner instruction:
publisher art and game text may be used with attribution (decision dated 2026-10-29).
This is an unofficial fan work and is not approved or endorsed by CD PROJEKT RED.
No new save writer or console/gameplay acceptance claim is introduced.

## Shipped content and budget

| Content | Catalogue records with icons | Unique files | New image bytes |
| --- | ---: | ---: | ---: |
| Common/sample items, consumables and equipment | 55 | 51 | 83,864 |
| Historical skill definitions and mutations | 107 | 107 | 170,234 |
| Gwent cards (canonical-index aliases share art) | 225 | 213 | 223,406 |
| Five regional maps | N/A | 5 | 921,066 |
| Total | 387 | 376 | **1,398,570** |

Images remain below the 4,000,000-byte plugin budget. Icons preserve aspect ratio and are at most
64×64 pixels, encoded as WebP quality 80. Identical outputs within each pack are stored once;
16 further catalogue references share identical art. Maps use WebP quality 72 and at most 2048 pixels per edge:

| Region | Dimensions | Bytes |
| --- | --- | ---: |
| White Orchard | 1280×1024 | 146,600 |
| Velen and Novigrad | 1792×2048 | 397,616 |
| Skellige | 2048×2048 | 134,696 |
| Kaer Morhen | 1024×1280 | 106,310 |
| Toussaint | 1280×1280 | 135,844 |

**107 ability/mutation descriptions** now contain wiki-transcribed game tooltips and
**43 item descriptions** include wiki-transcribed descriptions/effects. Original wiki introductory
prose remains on other items. Text remains CC BY-SA 3.0; publisher artwork has separate attribution.
The historical ability pages contain mixed patch annotations and removed ranks. Those values are
explicit historical references, never verified PS5 effects, legal rank/stack caps or write rules.
The current overhauled wiki trees were inspected and deliberately not joined to the older saved IDs.

## Sources and attribution

| Content | Pinned source / authors | Rights and modifications |
| --- | --- | --- |
| Item/skill icons | [Witcher Wiki](https://witcher.fandom.com/wiki/Category:The_Witcher_3_images), CD PROJEKT RED (art), wiki contributors (hosting); individual file URLs, retrieved image SHA-1 and actual input/output SHA-256 in the audit sidecar | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29. Selected exact native infobox IDs; historical ability name/file pairs; resized and WebP encoded. Wiki file revisions are recorded by download URL/cache timestamp and input hashes. |
| Crowns icon join | [Crown template, revision 611658](https://witcher.fandom.com/wiki/Template:Crown?oldid=611658), Witcher Wiki contributors | Explicit template uses `oren3.png` for third-game Crowns; this is not a guessed name-based currency join. Same publisher-art attribution. |
| 150 descriptions | Existing item article revisions and historical Combat/Signs/Alchemy/General revisions 649876–649879; mutations revision 617694, Witcher Wiki contributors | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/). Removed wiki markup/formatting and small-font annotations, retained tooltip/rank text with historical/PS5 caveats; item effects prepend existing descriptions. Each affected record has `descriptionSource` and `descriptionScope`. |
| Gwent art | [GwentTracker public/cards at d8eab8c](https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/public/cards), CD PROJEKT RED (art), Chris/Vishal Gupta and contributors (hosting) | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29. Exact `canonicalIndex`/native-index filenames, resized and deduplicated. The community catalogue's MIT licence does not relicense this artwork. |
| Regional backgrounds | [witcher3map-maps at df7fa74](https://github.com/witcher3map/witcher3map-maps/tree/df7fa74609df7587afc324e569d2574cf5b4f1ec), CD PROJEKT RED (art), DesignGears/hhrhhr (extraction), untamed0 and contributors (hosting/tile layout) | Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29. Host project identifies community work as CC BY-NC-SA; retained credit and terms. Only publisher map tiles imported; no community markers, coordinates, descriptions or code. |
| Tile orientation research | [custom.js at 437241a](https://github.com/witcher3map/witcher3map/blob/437241a978a13a1ca9114301d9ef887fadf4f260/assets/scripts/custom/custom.js), untamed0 and contributors | Read as text, not executed/copied. `tms: true` proves tile Y increases upwards. Assembly uses `pixelY = canvasHeight - (tileY + 1) × 256`; tiles are not internally flipped. |

[Publisher fan-content guidelines](https://www.cdprojektred.com/en/fan-content) are linked as
rights-holder information, not represented as a new licence grant. Owner policy is the recorded
repository basis; it does not transfer CD PROJEKT RED's rights or remove upstream terms.

The [machine-readable audit](SaveToolkit.Plugins.Witcher3/ReferencePacks/assets.source.json) records
every image's source, output hash, size and dimensions, plus original download hashes and each map
input tile hash. Its [sidecar](SaveToolkit.Plugins.Witcher3/ReferencePacks/assets.source.md) retains
the policy and processing notice. `Info.References`, separate F2 assets datasets, rich-cell source
details, map attribution and the framework Sources panel expose the corresponding provenance.

## Views, limits and fallback

F2 catalogue pack versions advance to 1.1.0; `witcher3.maps` starts at 1.0.0. `assetId` references
are relative to their own pack, so the existing F4 pickers receive resolved images automatically.
F3 inventory, ability, Gwent and catalogue rows have image cells. Raw identities and text remain
available when there is no image or pinned pack; unknown IDs receive no invented artwork.

The Map tags tab adds five lazy F9 reference maps. Native tag→region/name/coordinate joins remain
unknown. Each region explicitly says it is reference art without a saved overlay; **no saved marker
is assigned to a region or plotted**. Saved tags retain their independent bounded map/text list,
membership states and stable navigation. Map bounds are the processed art's pixel dimensions, not
a claimed native transform. Missing artwork retains an explanatory text alternative and external
regional guide. Backgrounds resolve solely through `references.GetAsset`/`AssetProvenance`.

All downloading/processing used Python `-I` and Pillow 12.2 in `%TEMP%/witcher3-assets`, outside the
repository. Image headers/formats were validated with Pillow before decoding; source tiles were
verified as 256×256 PNGs. A contact sheet was visually inspected and TMS orientation corrected before
import. No downloaded code was executed. Only selected processed images and normalized data were
copied into the plugin; no sample bytes, original dumps or processing scratch files were committed.
The JSON datasets are minified UTF-8; manifest text hashes normalize CRLF to LF as required by F2.

## Load measurement and validation

A temporary external Release console harness used the largest supplied sample (228,734 bytes).
Sample import was excluded. Each measured run constructed a fresh `ReferencePackStore`, pinned
packs, opened the sample, built the main view, queried inventory and expanded the first saved
ability/Gwent/map-tag pages. Stopwatch and `GC.GetTotalAllocatedBytes(true)` measured elapsed time
and total allocations. These are local smoke measurements, not statistical performance guarantees.

| Run | Before | After |
| --- | --- | --- |
| First load | 165.31 ms; 43,687,296 allocated bytes | 167.93 ms; 68,181,936 allocated bytes |
| Median of three following fresh-store loads | 51.40 ms; about 43.21 MB allocated | 70.77 ms; about 67.68 MB allocated |

The host validates and loads embedded packs once per store; document pins and image requests share
those validated objects. The measurements deliberately include that one-time validation cost each
run. No plugin metadata/image copy cache was added; regional view nodes resolve artwork lazily on
expansion. The F2 host loads image bytes eagerly at store construction, so this plugin cannot defer
the pack I/O/validation itself. Allocation totals include transient loader allocations, not retained
image memory. Tests assert repeated `GetAsset` returns the same object.

Validation covers every manifest hash, image header/type/dimensions and audit byte total; all
387 record `assetId`s resolve; sample rows expose expected image cells without changing bytes;
wiki text keeps its own licence/source; all five map backgrounds pass `SpatialViewValidation` and
missing-pack fallbacks preserve the text/list route. With
`SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`
exported in the same command, `dotnet test --project plugins/Witcher3/SaveToolkit.Plugins.Witcher3.Tests`
passes **115 tests, 0 failed, 0 skipped**. `dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -v quiet`
passes with **0 warnings, 0 errors**. Every plugin C# file is below 300 lines; manifest/file/byte totals,
documentation links and staged whitespace are checked. All repository changes are inside
`plugins/Witcher3/`; no push or merge is performed.
