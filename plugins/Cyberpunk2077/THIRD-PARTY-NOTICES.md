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

This MIT notice does not grant rights to CD PROJEKT RED creative assets. The separately attributed
asset pack below uses the owner decision 2026-10-29; no endorsement is implied.
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

## Publisher assets and spatial references

`ReferencePacks/cyberpunk2077.game-assets.en/` bundles 16 CD PROJEKT RED inventory icons,
146 explicit wiki item IDs/captions, 193 official CDPR planner descriptions and one map background.
Icons: **Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29**.
Map: **Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29**;
full holders: Piggyback Interactive Limited / CD PROJEKT RED. Official perk text retains CDPR rights
under that same owner-decision notice. This is a repository owner policy, not a licence grant or endorsement.

Sources: [Cyberpunk Wiki inventory images](https://cyberpunk.fandom.com/wiki/Category:Cyberpunk_2077_images_-_Inventory_Icons),
[CDPR planner](https://www.cyberpunk.net/en/build-planner),
[Piggyback Night City](https://maps.piggyback.com/cyberpunk-2077/maps/night-city), retrieved 2026-10-29.
The new wiki ID/title/caption adaptation remains **CC BY-SA 3.0**; retain attribution, source links,
change notices and share-alike terms when adapting it. Icons/images are separately reserved CDPR art.

Exact revisions, original image/JSON/tile hashes and URLs are in the pack's `upstream.json`.
Modifications: explicit base-ID hash joins, markup removal, ≤64px WebP icons and content deduplication,
49 native-index 256px public tiles assembled/recompressed into a 1,792px WebP. No runtime fetches.
No verified saved travel-ID/location join; markers retain the text list. Planner text is reference
material, not a version-matched PS5 rules dataset. Stored perk parsing still follows the existing
independent WolvenKit schema research; no downloaded implementation is copied or executed.
