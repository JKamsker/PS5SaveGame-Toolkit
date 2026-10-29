# Hogwarts inventory vacancy presentation

- Source: [HLSGE-derived saveGameDB.ts](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/saveGameDB.ts).
- Authors: HLSGE contributors / falker47 and contributors.
- Revision: `349c3f0b75e4d158e5b7d7920bded999b518de36`; retrieved 2026-10-29.
- Licence: No licence published; reused with attribution per owner decision 2026-10-28.
  The [embedded editor package](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/package.json)
  explicitly declares `UNLICENSED`; the outer desktop manager's MIT licence does
  not automatically cover this embedded upstream material.
- Research: combat-resource, mission-resource and gear-inventory queries exclude
  SQL NULL item IDs. This corroborates missing-item interpretation, not a universal
  zero-quantity rule. No upstream code was copied or executed.
- Implementation: original C# by PS5 Save Game Toolkit contributors,
  GPL-3.0-or-later. Only `InventoryDynamic` with the expected slot and payload
  columns opts in. Vacancy requires SQL NULL `ItemID`, SQL NULL `Variation`, and
  integer-zero `Count`. Other schemas and anomalous combinations remain visible.
- PS5 evidence: 30 images from 15 distinct PPSA01603 gameplay saves contain 44,572
  inventory records, 21,387 of this exact vacant shape and 30 named zero-count
  records. All named records remain visible. All 540 equipped records retain gear
  IDs, including 360 with the literal variation `None`; these are not flagged.
- Confidence: storage shape verified against supplied bytes; gameplay meaning
  Inferred. No console validation or unobserved sentinel semantics claimed.
- Presentation only: original rows, raw cells, tuple identities and no-op bytes
  are retained. Both diff paths include vacant slots. Search/filter/visibility
  run before sort/paging, and F6 query selection retains the visibility choice.
- Attribution also appears in the plugin README, `Info.References` and opted-in
  inventory table descriptions. No external dataset or media is embedded.
