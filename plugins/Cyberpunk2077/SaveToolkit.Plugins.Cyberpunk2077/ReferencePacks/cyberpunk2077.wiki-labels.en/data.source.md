# Cyberpunk Wiki base-record labels — CC BY-SA 3.0

Source: [Cyberpunk Wiki](https://cyberpunk.fandom.com/wiki/Cyberpunk_Wiki),
Cyberpunk Wiki contributors. Retrieved **2026-10-28** through the public MediaWiki API.
Copyright in the community text belongs to its contributors. This adapted dataset is
distributed under [Creative Commons Attribution-ShareAlike 3.0 Unported](https://creativecommons.org/licenses/by-sa/3.0/).
Keep attribution, source links, this notice and the same licence when redistributing adaptations.
No endorsement by the wiki, Fandom, contributors or CD PROJEKT RED is implied.

Licensing evidence: Fandom's [Help:Licensing revision 3983537](https://community.fandom.com/wiki/Help:Licensing?oldid=3983537)
states the default wiki text licence is CC BY-SA **3.0**, and excludes automatic licensing of images/video.
The Cyberpunk Wiki site's `action=query&meta=siteinfo&siprop=rightsinfo` response identifies CC-BY-SA.
`upstream.json` pins the licensing-page wikitext SHA-256, every source revision and its wikitext SHA-256.

| Page / contributors | Pinned revision |
| --- | --- |
| [Bounce Back](https://cyberpunk.fandom.com/wiki/Bounce_Back?oldid=297811) | 297811 |
| [MaxDoc](https://cyberpunk.fandom.com/wiki/MaxDoc?oldid=297812) | 297812 |
| [F-GX Frag Grenade](https://cyberpunk.fandom.com/wiki/F-GX_Frag_Grenade?oldid=301113) | 301113 |
| [EMP Grenade](https://cyberpunk.fandom.com/wiki/EMP_Grenade?oldid=301118) | 301118 |
| [X-22 Flashbang Grenade](https://cyberpunk.fandom.com/wiki/X-22_Flashbang_Grenade?oldid=287501) | 287501 |
| [Constitutional Arms Unity](https://cyberpunk.fandom.com/wiki/Constitutional_Arms_Unity?oldid=290048) | 290048 |

Changes: extracted **page titles and explicit `baseid` mappings only**, prefixed IDs with `Items.`,
computed exact CRC32/UTF-8-length keys, and intersected with the pinned MIT technical identifier catalog.
Result: **15 base-record labels**. No prose, database entries, descriptions, images, textures, gameplay
limits, stats or CDPR localization/art were copied. No inferred variant, clothing or cutscene-ID joins:
in particular `Items.Preset_V_Unity_Cutscene` retains its technical identifier because the Unity page
does not list that base ID. Labels describe base records, never final instance tier or safe writes.

The original offline generator is `../../../reference-data/generate-wiki-labels.py` relative to
this sidecar's directory. See the plugin's REFERENCES.md for exact reproduction steps. Input downloads
stay in scratch storage; no downloaded program was executed. The derived name dataset keeps its
CC BY-SA licence separately from plugin code and the MIT technical dictionaries.
