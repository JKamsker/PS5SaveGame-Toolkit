# BG3 starter reference data

Authors/sources: [bg3.wiki contributors](https://bg3.wiki/), and PS5 Save Game Toolkit
contributors (selection, annotations and SaveInfo origin-key mapping). Retrieved
2026-10-28. Each JSON record carries the individual source page; Gold, Potion of
Healing and Shortbow also pin the inspected wiki revision.

This adaptation (`templates.json`, `origins.json` and its annotations) is licensed
under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Retain this
attribution, the source links, modification notice and share-alike terms when
redistributing or adapting the pack. These data terms do not relicense plugin code.

Modifications: extracted factual names, categories, technical template GUIDs and
UIDs; keyed origins by the saved Origin strings; wrote new short annotations to
explain uncertainty. No wiki prose descriptions, character biographies, gameplay
limits, icons, artwork, markup or executable content are copied. Names and raw
identifiers are factual labels, not a licensed dump of the game's creative assets.

The wiki's [copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights) distinguishes
post-2024 dual-licensed contributions from legacy content that may remain
CC BY-NC-SA. This pack deliberately excludes legacy expressive text and images;
the current page footer alone is not proof that an old description is CC BY-SA.
Obtain a rights manifest before extending this pack with such content.

Scope: English labels, four exact template GUID candidates and three origin labels.
Build/module/load-order: unspecified; build-agnostic labels only. No gameplay rules,
inheritance, overrides, icons or limits are supplied. No Stats-ID fuzzy aliases are
used. Duplicate templates remain separate saved occurrences; names never establish
ownership. The three origin-key mappings are inferred labels, not live ECS joins;
only DarkUrge and Laezel occur in the current PS5 snapshot.
