# Illustrated references and Cradle atlas — retrieved 2026-10-29

The adjacent `visual-reference.source.json` is the exact source ledger: wiki file
IDs and upload timestamps, article revision URLs, original decoded RGBA pixel
SHA-256, image dimensions/crops, SHA-256 of the 64px PNG captures, source URLs,
and SHA-256 of the original downloaded map/coordinate snapshots. The manifest
hashes every final embedded image and catalogue. No sample payload is included.

## Images and text

Sources: [Pragmata Wiki item list](https://pragmata.fandom.com/wiki/Item?oldid=1830),
[weapon index](https://pragmata.fandom.com/wiki/Template:Navbox/Weapons?oldid=3084),
and the exact article/file-history URLs in the ledger. Authors: Pragmata Wiki
contributors and uploaders; Capcom owns the game art and quoted game text.
Wiki text/adaptations retain [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).
Game asset licence notice:
**Rights reserved by Capcom; used with attribution per owner decision 2026-10-29**.
This owner policy is not a claim that Capcom granted a redistribution licence.

29 images: 14 items and 15 common weapons. Thirteen are existing UI icons;
sixteen are transparent renders. Images were decoded by the collaborative browser,
alpha-cropped, fitted into transparent 64×64 PNG canvases with high-quality
resampling, and captured outside the repository. Pillow validates PNG signatures
and dimensions, then encodes WebP at quality 85 / method 6. No scripts or code from
the sources were executed. The ledger fingerprints decoded source pixels, not
original PNG byte streams; it separately hashes each captured thumbnail.

Nine short official-description excerpts are wiki transcriptions, unverified
against the PS5 game build. Six descriptions are explicitly labelled adapted wiki
summaries. Fourteen records have no supported description. No limits, unlocks or
saved ownership are inferred. Reference keys are stable `wiki-file:<pageid>`
identities. They are kept out of the numeric `pragmata-items` catalogue because
no source links these images to the save's 90 raw item IDs. The lazy F3 library
and browse-only F4 fields make this limitation visible alongside the data.

## Atlas and coordinates

Source: [Itemaps Pragmata map](https://itemaps.com/maps/pragmata),
[map image](https://itemaps.com/media/maps/pragmata/map.webp?v=20260916-1),
[coordinate snapshot](https://itemaps.com/media/maps/pragmata/markers.json?v=20261024-icons).
Authors/holders: Capcom (game map art); Itemaps / Kevin Henrique (atlas composition,
community location facts). Notice:
**Rights reserved by Capcom and Itemaps; used with attribution per owner decision 2026-10-29**.

The art is resized from 7168×2048 to 2048×585 outside the repository, encoded WebP
quality 85 / method 6. Only the 26 escape-hatch location IDs/names/regions/pixel
coordinates from the `the-cradle` plate are retained. Other 1,073 markers,
screenshots, narrative descriptions and scripts are not imported. Native
coordinates use the source's top-left origin; X rightwards, Y downwards:
`displayX = X * 2048 / 7168`, `displayY = Y * 585 / 2048`.
The distinct scales account for rounding the output height to 585 pixels.

The 26 positioned markers are community reference locations with Inferred
coordinate confidence and Unknown saved states. Existing save checkpoint hashes
retain their original keys/field targets without coordinates. No community marker
is claimed to match a stored checkpoint. Missing art/packs keep the existing
checkpoint list and external link. Both source and asset provenance are shown
by F9 and the Sources panel; text alternatives retain the distinction.

## Remaining priority gaps and research

- **43 mission descriptions:** the pinned MIT
  [mission JSON](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json)
  contains name/description message GUIDs. The
  [REE assets](https://github.com/kvasszn/ree-save-editor/tree/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata)
  contain technical strings, schema and enums, with no GUID-to-localization table.
  GitHub searches for Pragmata localization/datamines and the exact first mission
  name GUID found no usable public table. Wiki/gameplay display order is not used
  to guess a GUID association. All 43 mission names/descriptions remain unresolved.
- **Saved item/equipment icons:**
  [neuro-pragmata's pinned catalogue bindings](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings/catalogs.lua)
  resolve these via a live `GuiDataManager`/`ObjectIDs` instance, not a distributed
  ID table. The REE enums lack that mapping. Wiki file identities cannot prove
  PS5 inventory IDs. The 29 images are useful reference data pending that crosswalk.

Rebuild with `python -I plugins/Pragmata/scripts/build-visual-reference-pack.py <scratch>`.
Scratch must be outside the repository and contain the SHA-pinned captures,
`map.webp` and `markers.json` listed in the ledger. The script performs no network
requests or save I/O. Core validates and loads the pack once; the illustrated
table and background are requested only when their sections are expanded.
