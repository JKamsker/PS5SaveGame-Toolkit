# Nex map / summon annotations — MIT

Source: Nenkai and ffxvi-nex-layouts contributors,
[revision d3ad0627a29af168916ad574dadbca6759e2ac48](https://github.com/Nenkai/ffxvi-nex-layouts/tree/d3ad0627a29af168916ad574dadbca6759e2ac48).
Retrieved 2026-10-28. **MIT**, copyright (c) 2025 Nenkai.
Full notice: [THIRD_PARTY_NOTICES.md](../../../THIRD_PARTY_NOTICES.md).
Upstream [LICENSE](https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/LICENSE).

Datasets:
- `ffxvi_maps`: 107 entries from `GameMap.layout` set_comment annotations, plus
  the 602000 Hideaway 2 / 15020 pink-sky relationship documented above its columns.
  Distinct subrow annotations remain attributes of the base ID. No asset paths
  are invented or assumed unique. Annotations containing guesses/cutscene notes
  remain verbatim **research annotations**, not verified game UI names.
- `ffxvi_summons`: all 10 `SummonMode.layout` set_comment annotations. This names
  stored summon-mode IDs, not skill-rank byte indices or unlock-flag bits.

Modifications: extracted annotations, sorted numeric keys, grouped subrows,
retained source spelling/uncertainty; added explicit documented variant note.
No game dump/localization strings, third-party code execution, icons or textures.
English; build-agnostic research only; every joined field is **Inferred**.

Offline regeneration from a text-only scratch checkout at the pinned revision:
`python -I plugins/FinalFantasy16/research/build_layout_catalog.py <scratch>/ffxvi-nex-layouts`.
Generated JSON is UTF-8/LF and hashed in `pack.json`.
