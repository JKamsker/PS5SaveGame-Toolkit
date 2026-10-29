# ASTRO's PLAYROOM publisher assets — source record

Retrieved 2026-10-29. Publisher/game artwork and in-game captions: **Sony Interactive
Entertainment / Team ASOBI**. Capture authors and hosting sources are credited below.
Licence field for each publisher dataset:
`Rights reserved by Sony (SIE); used with attribution per owner decision 2026-10-29`.
Sony (SIE) means Sony Interactive Entertainment; the abbreviation fits F2's 100-character
licence field. This records the repository owner's reuse decision, not a publisher licence
or a claim that publisher assets became GPL/CC licensed. Publisher copyright notice:
https://www.playstation.com/en-us/legal/copyright-and-trademark-notice/ .
Existing catalogue names/aliases remain CC BY-SA 3.0, with their existing provenance in
`data.source.md`. The separate `astro_captions` dataset keeps publisher text separate.

## Artifact thumbnails and official captions

Source: https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/ ,
**PowerPyx / Gage**, published 2020-11-17. Exact source JPEG URLs below; pack SHA-256
hashes pin the imported derivatives. No live wiki revision or upstream asset hash is invented.
Downloaded JPEG headers, dimensions and decoder validity were checked with Pillow 12.2.0;
all artifact sources are 1024x576. Processing ran with Python `-I` in a `%TEMP%` scratch folder.
Files were treated as image data, never executed.

50 acquisition/museum thumbnails: crop boxes below (left, top, right, bottom) in original
source pixels; aspect-preserving Lanczos resize to at most **96x96**, WebP quality 78,
method 6, no metadata. The 96-pixel exception retains fine hardware detail (buttons,
ports and compact accessories). These are screenshot thumbnails, not invented transparent
cutouts. Source capture credit remains in the dataset, record details, accessible image
text and Sources panel. Identical output hashes are rejected by the regression test.

49 short official English game captions were manually transcribed from the linked images
into `captions.json` record `description` fields and joined to the existing schema-qualified
catalogue in the displayed row/inspection details. Capitalization/punctuation follows the
visible captions, with plain ASCII apostrophes and three-dot ellipses. The PlayStation
Mouse screenshot shows a placed museum item, not a readable caption: its description is
not invented. The PocketStation caption uses screenshot 138, rather than placed-item 139.
Candidate save-ID joins remain **Inferred**, including `ds3`; captions do not validate
ownership semantics, physical pickup locations or linked gameplay effects.

| Saved artifact alias | Exact screenshot | Crop (pixels) |
| --- | --- | --- |
| `psone` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-151.jpg | `(250, 130, 770, 460)` |
| `ps2_broadband_adapter` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-152.jpg | `(250, 130, 770, 460)` |
| `psp_camera` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-157.jpg | `(250, 130, 770, 460)` |
| `psp_microphone` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-159.jpg | `(250, 130, 770, 460)` |
| `psp_gps` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-154.jpg | `(250, 130, 770, 460)` |
| `psp_go` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-158.jpg | `(250, 130, 770, 460)` |
| `ps3_slim` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-155.jpg | `(250, 130, 770, 460)` |
| `ps3_super_slim` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-156.jpg | `(250, 130, 770, 460)` |
| `ps4_slim` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-160.jpg | `(250, 130, 770, 460)` |
| `ps4_pro` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-153.jpg | `(250, 130, 770, 460)` |
| `ds3` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-4.jpg | `(250, 130, 770, 460)` |
| `pseye` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-7.jpg | `(250, 130, 770, 460)` |
| `move_sharp_shooter` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-14.jpg | `(250, 130, 770, 460)` |
| `move_shooting_attachment` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-16.jpg | `(250, 130, 770, 460)` |
| `move_navigation_controller` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-20.jpg | `(250, 130, 770, 460)` |
| `ps3_bluray_disc` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-22.jpg | `(250, 130, 770, 460)` |
| `psp_umd` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-30.jpg | `(250, 130, 770, 460)` |
| `psp` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-31.jpg | `(250, 130, 770, 460)` |
| `ps3` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-34.jpg | `(250, 130, 770, 460)` |
| `move` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-36.jpg | `(250, 130, 770, 460)` |
| `ps_vita_card` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-43.jpg | `(250, 130, 770, 460)` |
| `ps4_camera` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-49.jpg | `(250, 130, 770, 460)` |
| `ds4` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-53.jpg | `(250, 130, 770, 460)` |
| `psvr_processing_unit` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-57.jpg | `(250, 130, 770, 460)` |
| `ps_vita` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-60.jpg | `(250, 130, 770, 460)` |
| `psvr` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-66.jpg | `(250, 130, 770, 460)` |
| `psvr_aim_controller` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-70.jpg | `(250, 130, 770, 460)` |
| `ps4` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-71.jpg | `(250, 130, 770, 460)` |
| `buzz_controller` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-74.jpg | `(250, 130, 770, 460)` |
| `ds2` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-78.jpg | `(250, 130, 770, 460)` |
| `ps2_multitap` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-82.jpg | `(250, 130, 770, 460)` |
| `ps2_memory_card` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-86.jpg | `(250, 130, 770, 460)` |
| `ps2_bluedisc` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-93.jpg | `(250, 130, 770, 460)` |
| `eyetoy` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-98.jpg | `(250, 130, 770, 460)` |
| `ps2_slim` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-101.jpg | `(250, 130, 770, 460)` |
| `singstar_mic` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-105.jpg | `(250, 130, 770, 460)` |
| `ps2` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-106.jpg | `(250, 130, 770, 460)` |
| `ps1_controller` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-110.jpg | `(250, 130, 770, 460)` |
| `ps1_mouse` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-114.jpg | `(790, 395, 970, 575)` |
| `ps1_memory_card` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-118.jpg | `(250, 130, 770, 460)` |
| `ps1_blackdisc` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-120.jpg | `(250, 130, 770, 460)` |
| `psone_screen` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-126.jpg | `(250, 130, 770, 460)` |
| `ps1_multitap` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-129.jpg | `(250, 130, 770, 460)` |
| `ds` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-135.jpg | `(250, 130, 770, 460)` |
| `pocket_station` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-138.jpg | `(250, 130, 770, 460)` |
| `ps1` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-140.jpg | `(250, 130, 770, 460)` |
| `ps5_sparkv_headphones` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-144.jpg | `(250, 130, 770, 460)` |
| `ps5_camera` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-141.jpg | `(250, 130, 770, 460)` |
| `ps5_ds5` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-142.jpg | `(250, 130, 770, 460)` |
| `ps5` | https://www.powerpyx.com/wp-content/uploads/Astros-Playroom-Collectibles-143.jpg | `(250, 130, 770, 460)` |

## Shared puzzle-piece illustration

Source: https://www.pushsquare.com/guides/astros-playroom-memory-meadow-all-collectibles-artefacts-puzzle-pieces ,
**Push Square / Sammy Barker**, page checked 2026-10-29.
Exact JPEG: https://images.pushsquare.com/3100505fd73ae/astros-playroom-memory-meadow-gusty-gateway-puzzle-piece-2.large.jpg .
Original 1920x1080; crop (855,275)-(1005,415); resized to 64x60, WebP quality 82, method 6.
One generic category illustration is shared by all 96 puzzle records. It is **not** a
physical-location or individual-piece-ID join. No image is assigned to the 18 unresolved
prop/prize identities.

## World-selection map background

Source: https://www.actugaming.net/guide-emplacement-quatre-bots-speciaux-astros-playroom-667952/ ,
**ActuGaming / Alexis**, published 2024-09-04; checked 2026-10-29.
Exact JPEG: https://static.actugaming.net/media/2024/09/astros-playroom-bots-speciaux-lady-maria-1.jpg .
Original 1920x1080 French world-selection screenshot; cropped (40,50)-(1180,980), yielding
1140x930; WebP quality 76, method 6, no metadata. Crop removes coin/puzzle counters and
most menu labels. Remaining crowns, highlight and button artwork are **static source
state**, never an assertion about the current save; the map text explicitly says this.

`AstroWorldMap.cs` records manually digitized tile centers in original source pixels and
an explicit `MapTransform(1,1,-40,-50)`. IsVisited/IsCleared markers are displaced -12/+12
pixels horizontally so both can be selected when a schema exposes both. Every mapped
stage point was visually checked against the cropped diagram. Original digitization is
GPL-3.0-or-later; candidate stage aliases remain CC BY-SA 3.0 / Inferred. This is a level
selection diagram, not a physical level or collectible map and not a prerequisite graph.
World aggregates without a tile stay unpositioned in the marker list. Missing assets or
unknown schemas use the original eight-column schematic; all original no-coordinate
artifact/puzzle/prop lists remain available.

## Size and loading

52 unique WebP images, **155,296 bytes** total: artifact images and puzzle image **64,884
bytes**, map **90,412 bytes**. No original JPEGs or private save data are embedded.
The existing F2 store validates and loads one immutable pack at store initialization,
then shares the same AssetContent buffers across pinned contexts/documents. Plugin code
does no filesystem/network I/O. Map creation/asset resolution is lazy on map expansion;
image HTTP loads are lazy in the existing F3 renderer. The plugin cannot defer the host's
embedded-pack startup load without a framework change.
