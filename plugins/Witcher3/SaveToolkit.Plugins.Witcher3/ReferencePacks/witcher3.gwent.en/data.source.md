# Gwent community catalog — MIT

Source: https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/data
Retrieved 2026-10-28. Authors: Chris (rfvgyhn/gwent-tracker), Vishal Gupta and contributors.

`gwent.json` adapts all 215 named six-faction YAML definitions and 10 explicit index aliases.
Faction/type, acquisition, expected copies and missable quest/cutoff advice derive from
`catalog/*.yml` and `missable.yml`; reference strength/row/ability annotations derive from
`card-stats.json` at the same commit. Targets are not legal save/deck/achievement limits.

Modifications: expanded aliases into separate native keys; normalized descriptions and attributes;
retained canonical-index relations; added confidence, availability and write-limit caveats.
No artwork, flavor text, script bodies or achievement-rule execution is bundled.

Keep both full MIT notices when redistributing:

- [2017 Chris catalog license](../../../GwentCatalog-LICENSE.txt).
- [2026 Vishal Gupta project license](../../../GwentTracker-LICENSE.txt).

See [catalog scope and methodology](../../../CATALOG.md) for unresolved indices and evidence.
