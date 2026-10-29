# Yōtei screenshot symbols and local map excerpt

Dataset: `suckerpunch_yotei_art`, pack `suckerpunch.yotei` 1.0.0, retrieved 2026-10-29.
Source page: [PowerPyx, Ghost of Yotei All Hot Spring Locations](https://www.powerpyx.com/ghost-of-yotei-all-hot-spring-locations/), published 2025-10-01.
Original image: [Horizon View screenshot](https://www.powerpyx.com/wp-content/uploads/ghost-of-yotei-hot-spring-1.jpg).
Authors/holders: PowerPyx; Sony Interactive Entertainment / Sucker Punch Productions (game imagery).
[Source terms](https://www.powerpyx.com/terms-of-service/).
Rights reserved by PowerPyx / SIE; used with attribution per owner decision 2026-10-29.
This records owner reuse policy, not a licence grant. SIE abbreviates Sony Interactive Entertainment
because manifest licence labels are limited to 100 characters.

The original JPEG is 1920×1080, 84,037 bytes, SHA-256
`0a8f913b497585a30c095bb84b810c3b0cb0de8b7579ea6e2006734e30169ab9`.
Image headers were verified with Pillow. Processing ran outside the repository, using Python `-I`.
The map retains the original dimensions/projection/orientation; WebP quality 70. The three symbols
are tight crops of visible in-game UI, WebP quality 85, with no fabricated art. Crop rectangles below
are half-open original image pixels. No downloaded code was executed.

| Local asset | Dimensions | Bytes | Original crop / binding | SHA-256 |
| --- | --- | ---: | --- | --- |
| `icons/mon.webp` | 35×38 | 464 | (158,37)–(193,75); Mon, Inferred lexical mapping | `2fc7fde57892a0ebc970d49be5923c8cc661f7527a8f933e763235c762a958b0` |
| `icons/flowers.webp` | 38×38 | 370 | (34,37)–(72,75); Flowers, Inferred lexical mapping | `a209469f2f10174e1c349b74aa185316787ea72263c0a3f2795a465f6a4ef097` |
| `icons/hot-spring.webp` | 36×36 | 696 | (941,533)–(977,569); 16 reference-only hot spring guide records | `4f9e48480626284bfc6239597a5462f98d46c9a6fd9997f84bd095147d65ad00` |
| `maps/horizon-view.webp` | 1920×1080 | 88,172 | Uncropped full screenshot; F9 local map excerpt, Horizon View pin only | `47300afcc329f2d5a895882feaec4807dee0521b6a5cbc7675b96cc539c86529` |

**Total new binary assets: 89,702 bytes**, three symbols and one map excerpt. Combined with the
existing Tsushima pack: 328,439 bytes. All symbols are below 64×64, and the map is below 2048 pixels
on its long edge and below 600 KB. No duplicate binary assets are included.

The map is a local screenshot excerpt, not a whole-island map. Only its visible Horizon View pin has
position (960,550); the 15 other guide locations have no coordinates. Saved world transforms and
all discovery/completion/reward states are Unknown. The missing-art fallback retains the guide table.
Every asset is declared with a hash in the manifest and has visible dataset/map/record provenance.
