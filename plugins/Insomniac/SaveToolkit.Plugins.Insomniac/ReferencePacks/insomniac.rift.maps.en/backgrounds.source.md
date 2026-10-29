# Regional background provenance — 2026-10-29

**Guides4Gamers**, with publisher material by **Insomniac Games / Sony Interactive
Entertainment (SIE)**. Rights reserved by Guides4Gamers/SIE; used with attribution
per owner decision 2026-10-29. The author field retains the full publisher names;
the short licence field respects the SDK's 100-character limit. This records the
owner instruction, not an upstream licence grant.

Source: [Guides4Gamers regional maps](https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/).
The nine page links and 403 marker snapshot pins remain in `markers.source.md`.
`backgrounds.provenance.json` pins every downloaded tile URL and SHA-256, source
map ID/version, native dimensions, output dimensions, output SHA-256 and size.
Tile versions exactly match the existing marker snapshot versions.

The renderer's documented/read coordinate path was inspected as text, never
executed or copied. Its tiles use
`https://guides4gamers.com/sites/31/maps/<id>-<version>/slices/2/<column>x<row>.jpg`.
Level 2 scales source coordinates by 1/2; a full tile is 512 pixels and edge
tiles may be shorter. Tiles are placed at `(column*512, row*512)` on a canvas of
`(nativeWidth/2, nativeHeight/2)`, preserving **all edges without crop or padding**.
The complete canvas is downscaled uniformly within 2048 × 2048, with normal
integer pixel rounding, and encoded as WebP quality 76, method 6. No guide markers
are baked into the images. The source art's titles and separate dimension layouts
are preserved. Source JPEG headers, decoded dimensions and final WebP were checked
outside the repository using Python `-I` and Pillow.

F9 keeps original source-pixel bounds and an explicit identity transform. Its
background fills precisely those bounds; the renderer scales the full image to
that rectangle. The image and all 403 markers therefore share the same native
extent and axes. Corson V marker 120/26577 remains at (1885, 1308) in a 4096×3584
extent, corresponding to (942.5, 654) in its 2048×1792 background. No PS5 save/fog
or gameplay-world coordinate transform is asserted. Pickup states stay Unknown.

A nine-region contact sheet with all marker overlays was inspected after
stitching. Source locations can be outside the main landmass (pocket dimensions,
arena/reward groups); they are retained exactly, not moved to an invented landmark.
Backgrounds pass SDK spatial validation through Core for every primary sample.
If assets/provenance are absent, the same coordinate-only maps and text lists
remain. Nine backgrounds total **1,165,708 bytes**, each below 600,000 bytes.
Background bytes are loaded/validated once by the F2 store. A background is only
resolved by plugin code when its region expands; plugin code performs no I/O.
