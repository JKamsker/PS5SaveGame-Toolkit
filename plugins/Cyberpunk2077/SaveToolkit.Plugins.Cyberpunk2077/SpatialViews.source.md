# Spatial views: sources and non-imported external data

Retrieved / rechecked **2026-10-28**. Implemented independently in `PerkGraphInspection.cs` and
`TravelMapInspection.cs`. No extra dataset, downloaded code, artwork or coordinate file is bundled.

| Source | Author / rights | Use / modifications |
| --- | --- | --- |
| [Official Night City map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city) | Piggyback Interactive Limited / CD PROJEKT RED; all rights reserved; no redistribution permission inferred | HTTPS external link only, with host/attribution in the shared F9 renderer. No map art, tiles, coordinates, prose, location IDs or geographic joins imported. External build coverage is unknown. |
| [SNewPerk](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SNewPerk.cs) and [SAttributeData](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SAttributeData.cs) | WolvenKit contributors, [GPL-3.0](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/LICENSE) | Format research: stored group/type/unlockedPerks/currLevel. Existing independently implemented reader reused; no schema source or implementation copied. User-save identifiers/ranks are displayed with the existing schema-source attribution. No prerequisite edge can be inferred from membership. |
| [Official build planner](https://www.cyberpunk.net/en/build-planner), [announcement](https://www.cyberpunk.net/en/news/48958/build-planner-craft-your-cyberpunk-2077-and-phantom-liberty-builds) | CD PROJEKT RED; proprietary; no redistribution permission inferred | Research/external documentation link only; no descriptions, perk rules, assets or data copied. Its current content is not a version-pinned PS5 2.21 rule source. |
| Existing F2 travel identifiers | DeweySalt / Deweh and contributors, MIT | Reuse the exact catalog/saved union and per-record attribution documented in `ReferencePacks/cyberpunk2077.identifiers/data.source.md`. Marker IDs are full decimal TweakDBIDs. No newly researched location names or coordinates. |

Positions are omitted; bounds are an empty 0..1 display extent, not a city transform. Found denotes
saved registration only; all unregistered/unavailable cases are Unknown. Graph states are Unknown
because active effects and eligibility are unproven. Every spatial node has a text alternative and
links to an independently available read-only field; no action is executed by navigation.

The root README source table, `Info.References`, third-party notices, per-node descriptions and
external-link attribution retain these source links and limits. Full existing catalog attribution
remains in F2 Sources and in linked details, even when a spatial description is size-bounded.
