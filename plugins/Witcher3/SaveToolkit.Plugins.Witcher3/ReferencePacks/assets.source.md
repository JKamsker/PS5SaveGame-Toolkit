# Publisher images and wiki-transcribed game text — 2026-10-29

**Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29.**
This owner policy is the repository basis for inclusion, not a transfer of publisher rights.
[Publisher fan-content guidelines](https://www.cdprojektred.com/en/fan-content).
This is an unofficial fan work and is not approved or endorsed by CD PROJEKT RED.

Authors: CD PROJEKT RED (game art/text); Witcher Wiki contributors (hosting and tooltip transcription);
Chris, Vishal Gupta and GwentTracker contributors (card hosting/catalogue); DesignGears/hhrhhr
(map extraction); untamed0 and witcher3map contributors (map hosting/tile layout).
Adaptation: PS5 Save Game Toolkit contributors. Retrieved 2026-10-29.

Sources:

- Witcher Wiki file pages and pinned article revisions: every exact file URL, cache timestamp,
  retrieved upstream SHA-1, actual downloaded SHA-256, output SHA-256, size and dimensions appear
  in [assets.source.json](assets.source.json). Some CDN responses are resized/transcoded, so the
  recorded download SHA-256 identifies the actual processed input independently of upstream SHA-1.
- Crowns uses [Template:Crown revision 611658](https://witcher.fandom.com/wiki/Template:Crown?oldid=611658).
- [GwentTracker card art, d8eab8c](https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/public/cards).
- [Regional map tiles, df7fa74](https://github.com/witcher3map/witcher3map-maps/tree/df7fa74609df7587afc324e569d2574cf5b4f1ec).
- [TMS orientation research, 437241a](https://github.com/witcher3map/witcher3map/blob/437241a978a13a1ca9114301d9ef887fadf4f260/assets/scripts/custom/custom.js).

Modifications: exact item infobox/native IDs and historical ability name/file joins; native/canonical
Gwent index filenames; bounded 64×64 WebP icons (quality 80), deduplicated byte-identical outputs.
Regional zoom-3 PNG tiles were stitched using verified TMS Y reversal, composited onto dark parchment
and encoded as WebP quality 72, maximum 2048 pixels per edge. Every map input tile has a hash in
the audit. Map artwork is reference-only: native saved-tag/region/position joins are unknown and
no saved marker is plotted. No community coordinate/marker/text dataset or code was imported.
The map host identifies its community work as CC BY-NC-SA; retain its attribution and terms.

Processing used isolated Python `-I` and Pillow 12.2 in a temporary folder outside the repo, with
header/type validation before decoding and visual QA before import. No downloaded code was run.
There are 371 icons and five maps totaling **1,398,570 image bytes**; all remain local F2 assets.

Wiki-transcribed ability tooltips and item effects retain **CC BY-SA 3.0**, their pinned per-record
`descriptionSource`, `descriptionScope`, existing article attribution and share-alike notices.
Historical/mixed-patch numerical text does not establish PS5 gameplay rules. Gwent community
metadata keeps its separate MIT notices; those terms do not relicense publisher card art.
See [ASSETS.md](../../ASSETS.md) for exact counts, source table, benchmark and validation scope.
