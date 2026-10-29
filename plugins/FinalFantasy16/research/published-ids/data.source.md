# Published Final Fantasy XVI identifier CSV facts

Source/author: [Nenkai and ffxvi-modding contributors](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids).
Pinned revision: `4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03`.
Retrieved: 2026-10-29. Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
The source-tree link records the absence of published terms; it is not a licence grant.
This notice does not claim to relicense upstream data as MIT or CC BY-SA.

The five CSV files in this folder retain the published identifier/name facts
verbatim. They contain no game save bytes, media, descriptions, dialogue or
upstream executable code. Downloads were inspected as text in temporary storage;
only the reviewed reference data is imported here. No third-party code was run.

SHA-256 after CRLF-to-LF normalization (the downloaded bytes already used LF):

| File | Rows | SHA-256 |
| --- | --- | --- |
| [item_ids.csv](https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/item_ids.csv) | 373 | `90a39daa4656115ac79fd354ae1593634962dc0171b2bef94e568f6e407f60c1` |
| [equip_item_ids.csv](https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/equip_item_ids.csv) | 537 | `fba2cff3e6ac4781a26cda158456aad174a17d2fb3ea1bcfd52830ce94579355` |
| [gamemap_ids.csv](https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/gamemap_ids.csv) | 150 | `b7a5d48c040427703437d546b866c7b0a1aa71d86ff7b892c7dc6cd36cea627a` |
| [bnpc_ids.csv](https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/bnpc_ids.csv) | 1758 | `009b194bf58b1333fe992b4b28aca4fe1ccd84dd2f8b32e36d3b69f1a0b73a1b` |
| [enpc_ids.csv](https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/enpc_ids.csv) | 1085 | `54d6c50d2cb773d1bd13bfb74b633e90b29ee43bf5513823e18460f52e897bc4` |

The plugin-owned [converter](../build_published_catalog.py) verifies every pinned
hash before building the [F2 pack](../../SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.published.en/data.source.md).
The upstream Markdown documents SQL-like extraction queries: items/equipment use
`Key, Name`, maps join `gamemap` to `placename`, and NPC lists include only
speaker-linked rows. These are partial community lists, with no demonstrated
PS5 build/patch availability or NPC-instance/save-index mapping.

No code or other source was previously excluded solely for having no licence.
The identifier import contains no media. The separate publisher-media decisions
are now resolved by the attributed imports documented in
[game-assets.source.md](../game-assets.source.md); they are not continuing licence refusals.
