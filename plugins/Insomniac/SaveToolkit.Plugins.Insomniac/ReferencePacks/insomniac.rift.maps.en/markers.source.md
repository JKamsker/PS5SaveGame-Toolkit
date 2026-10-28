# Guides4Gamers marker snapshot provenance

Author: Guides4Gamers, a community guide author. Retrieved 2026-10-28 (UTC).
Source index: https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/
Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
The source footer says "All rights reserved." This records the owner's reuse
instruction; it does not claim a licence grant from the source author.
Terms/source notice: https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/

403 factual marker records from nine regions are embedded in `markers.json`.
Each record retains the map ID, marker ID, source category, label, native X/Y,
map width/height, map version, source URL and raw-response SHA-256. Upstream does
not publish immutable revision URLs: the pin is **retrieval date + map ID/version
+ raw-response hash**, rather than an assertion that the live URL is immutable.
Dataset SHA-256 is in `pack.json` (LF-normalized JSON).

## Pinned snapshots

Download endpoint for each ID: `https://guides4gamers.com/json/map.2.0.php?id=<ID>`.
Regional page: `https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/<slug>/`.
The slug is stored in every record; these pages are also in Info.References.

| Region | Map ID / version | Pixel dimensions | Markers | Raw response SHA-256 |
| --- | --- | --- | --- | --- |
| Corson V | 120 / 2 | 4096 x 3584 | 31 | `a5e263afe4653aed0af5b90c516a2654895eb911200d0aecb05baf639e2e5511` |
| Sargasso | 123 / 3 | 4096 x 3584 | 93 | `ff48d18cc1e18c5ddb4cfecc896be6d3b507058df9b61725104116afc8bea67c` |
| Scarstu Debris Field | 124 / 3 | 4096 x 3584 | 20 | `e6ef6d79dba999d63573b1a0f73fc419dd0abfae50ca6068376e23267b655b81` |
| Savali | 125 / 1 | 4096 x 4096 | 56 | `af2d93a9037e37362423ae4f7562a00a0d392f78212a93eccf6e1003a60e6173` |
| Blizar Prime | 126 / 1 | 6144 x 4096 | 78 | `911debfcfbb1904fd02553b9d8f7b87efef9b3ba090cec529f4f899677e8fd17` |
| Torren IV | 127 / 1 | 4096 x 3584 | 23 | `b2019676ee72f07deaf49bf4d1fd403290ee16b1034106cc5d9e03ea4b8ad9d4` |
| Cordelion | 128 / 1 | 5120 x 4096 | 41 | `e40e8c68462539a985b91f253bb7875a9b40e5ca864522d0bf104db3927a6356` |
| Ardolis | 130 / 1 | 5120 x 4096 | 30 | `daa74bfddc017ec2339de598099fd45bdd3bcebcb661dace23078d3272f2cb47` |
| Viceron | 131 / 1 | 3072 x 6144 | 31 | `c716ee06f924b33009282f8a9b69881a92c8e65cd7286e8c6878525ed88dc642` |

## Extraction and exclusions

Parse the JSON as data without executing downloaded scripts. Select `pois` map
keys and their `name`, `x`, `y`; join the numeric `icon` to `legend.data.id` for a
category name only. Retain `map.id`, `map.path`, `map.version`, `map.width` and
`map.height`. Sort by map ID, then numeric marker ID; key records as
`<map-id>/<marker-id>`. Preserve repeated labels with distinct native identities.
Add original uncertainty descriptions; do not copy upstream HTML descriptions.
No icons, image URLs, tiles, screenshots, path geometry, quests, game dialogue,
art or source implementations are included. No assets dataset is distributed.

The source renderer was read, not executed: `loadMapData` loads the dimensions
and POIs above; rendering adds a screen offset to native X/Y multiplied by scale.
Thus the F9 coordinate space uses top-left-origin map pixels with X right and Y
down, explicit identity transform, and original source width/height. This is
community guide space, **not** a calibrated PS5 save/fog/world transform. Map
backgrounds remain absent. No upstream map renderer implementation was copied.

## Verification and confidence

Tests pin counts for all nine regions plus the Corson V marker 26577 vector
(1885, 1308), verify every dataset hash through the F2 loader, validate all marker
bounds/IDs/categories/provenance and Core F9 lazy expansion, and round-trip all
36 primary gameplay saves unchanged. The existing conformance suite preserves
all 76 supplied gameplay/preferences records including backups/generations.

Measured PS5 masks still expose every stored bit, including unresolved slots.
There is **no demonstrated guide marker ID to stored bit/hash join** in the
samples. Guide locations are Inferred and all pickup statuses remain Unknown;
source `status` is deliberately not treated as save progress. Guide counts are
marker counts, not item quantities, completion totals or evidence of ownership.
The source lists 36 Sargasso Zurpstone markers although gameplay requires 60
stones: no completeness or one-marker-per-item claim is made. Controlled pickup
pairs and three non-collinear landmarks per physical transform remain in
OWNER-CAPTURES.md / issue #31. Existing writes and warnings are unchanged.
