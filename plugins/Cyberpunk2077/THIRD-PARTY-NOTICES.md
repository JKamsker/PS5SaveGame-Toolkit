# Third-party notices

## Community identifier dictionaries

`SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks/cyberpunk2077.identifiers/`
adapts factual item, category, fact, vehicle and fast-travel identifiers from
[Deweh/CyberCAT-SimpleGUI](https://github.com/Deweh/CyberCAT-SimpleGUI/tree/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2),
retrieved 2026-10-28. Copyright (c) 2021 DeweySalt, MIT.
The full licence is retained in [CP2077SaveEditor-LICENSE.txt](CP2077SaveEditor-LICENSE.txt).
The baseline `Resources/ItemNames.json` dictionary has the same upstream provenance.
Dataset-specific source URLs, exact modifications and scope are recorded beside
the data in `data.source.md` / `ItemNames.source.md`, the pack manifest, and README.
Keep those attribution and licence notices with adapted data.

This notice does not grant rights to CD PROJEKT RED creative assets. No icons,
textures, localized prose, dialogue or extracted game-resource files are bundled.
The MIT application licence does not relicense its GPL WolvenKit dependencies.
No code from those dependencies or from upstream generators was copied/executed.

## Existing container research and codec

Existing CSAV layout/facts research cites [WolvenKit/CyberCAT](https://github.com/WolvenKit/CyberCAT),
MIT; its full notice remains in [CyberCAT-LICENSE.txt](CyberCAT-LICENSE.txt).
The plugin references `K4os.Compression.LZ4` 1.3.8, MIT:
<https://github.com/MiloszKrajewski/K4os.Compression.LZ4/blob/master/LICENSE>.
WolvenKit GPL schemas are research references only, as documented in the README.

## Cyberpunk Wiki base-record labels

`ReferencePacks/cyberpunk2077.wiki-labels.en/item-labels.json` adapts titles and explicit base IDs
from six Cyberpunk Wiki pages by **Cyberpunk Wiki contributors**, retrieved 2026-10-28.
The derived dataset remains [CC BY-SA 3.0 Unported](https://creativecommons.org/licenses/by-sa/3.0/).
Redistributed adaptations must retain attribution, licence/source links and share-alike terms.
No contributor, Fandom or CD PROJEKT RED endorsement is implied.

Exact source revisions, source-content SHA-256 hashes and modifications are in the pack's
`data.source.md`, `upstream.json`, F2 manifest and README table. Every named row also exposes the
record source and attribution. This pack has 15 base-record names, no copied descriptions,
database prose, images, art or game-resource/localization exports. It does not change the licence
of unrelated plugin code or the separately attributed MIT technical dictionaries.

Fandom's [Help:Licensing revision 3983537](https://community.fandom.com/wiki/Help:Licensing?oldid=3983537)
states its default text licence is CC BY-SA 3.0; images/video are not automatically covered.

## External spatial references (not imported)

The F9 map links to [Piggyback's official Night City map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city),
by Piggyback Interactive Limited / CD PROJEKT RED, all rights reserved. No map assets, coordinates,
location IDs or prose are bundled. The [official build planner](https://www.cyberpunk.net/en/build-planner)
is an external research/documentation reference, not an embedded rules dataset. Consulted 2026-10-28.
Stored perk views reuse independently implemented parsing researched from the pinned WolvenKit
SAttributeData/SNewPerk schemas (GPL-3.0; no source copied). See `SpatialViews.source.md` beside the code.
