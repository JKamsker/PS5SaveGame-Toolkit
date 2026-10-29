# Spatial views: sources, attributed artwork and reference text

Retrieved / rechecked **2026-10-29**. Implemented independently in `PerkGraphInspection.cs` and
`TravelMapInspection.cs`. The new attributed art/text pack is documented in
[data.source.md](ReferencePacks/cyberpunk2077.game-assets.en/data.source.md), with exact input
hashes/revisions in its `upstream.json`. No downloaded code executes; no coordinate crosswalk is bundled.

| Source | Author / rights | Use / modifications |
| --- | --- | --- |
| [Official Night City map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city) | Piggyback Interactive Limited / CD PROJEKT RED. Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29 | 49 public zoom-3 tiles assembled at native tile indices into a local 1,792px WebP, 557,120 bytes. No saved-ID/geographic crosswalk; markers remain unpositioned in the text list. External source link and missing-pack fallback retained. |
| [SNewPerk](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SNewPerk.cs) and [SAttributeData](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SAttributeData.cs) | WolvenKit contributors, [GPL-3.0](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/LICENSE) | Format research: stored group/type/unlockedPerks/currLevel. Existing independently implemented reader reused; no schema source or implementation copied. User-save identifiers/ranks are displayed with the existing schema-source attribution. No prerequisite edge can be inferred from membership. |
| [Official build planner](https://www.cyberpunk.net/en/build-planner), [English chunk](https://www.cyberpunk.net/build/8603-b6e6f72c476c734a52a4.js) | CD PROJEKT RED. Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29 | 193 exact enum/name/description pairs parsed as JSON, emphasis tags removed. Stored perk identity/ranks stay unchanged. Current planner text is reference material, not version-pinned PS5 2.21 prerequisite/effect validation. |
| Existing F2 travel identifiers | DeweySalt / Deweh and contributors, MIT | Reuse the exact catalog/saved union and per-record attribution documented in `ReferencePacks/cyberpunk2077.identifiers/data.source.md`. Marker IDs are full decimal TweakDBIDs. No newly researched location names or coordinates. |

Positions are omitted; bounds are a 0..1 image display extent, not a city transform. Found denotes
saved registration only; all unregistered/unavailable cases are Unknown. Graph states are Unknown
because active effects and eligibility are unproven. Every spatial node has a text alternative and
links to an independently available read-only field; no action is executed by navigation.

The root README source table, `Info.References`, third-party notices, per-node descriptions and
external-link attribution retain these source links and limits. Full existing catalog attribution
remains in F2 Sources and in linked details, even when a spatial description is size-bounded.
