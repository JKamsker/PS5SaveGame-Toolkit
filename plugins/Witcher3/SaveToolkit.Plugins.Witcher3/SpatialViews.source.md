# Spatial projections and external maps

Author: PS5 Save Game Toolkit contributors. Inspected 2026-10-28.
Original projection code: repository GPL-3.0-or-later, https://www.gnu.org/licenses/gpl-3.0.html .
Source: https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/plugins/Witcher3 .

Skill/mutation identities, ranks, branches, costs and `requiredMutations` edges are decoded from
the document's own native saved definitions. No saved bytes or extracted game-resource dataset is
redistributed here. No skill tier is converted into a prerequisite. Mutation availability/research
is unknown. Native map tags come only from `CCommonMapManager`'s explicitly counted
`KnownMapPinTags`, `DiscoveredMapPinTags` and `DisabledMapPinTags` blocks. Membership is not
completion, fast-travel eligibility or an awarded point. No region or coordinates are guessed.

Friendly ability labels/descriptions come from the existing pinned Witcher Wiki pack. Its
`ReferenceRecord.AttributionText` is retained in every graph description and reference field;
the underlying derived dataset remains CC BY-SA 3.0, with its existing source sidecar/index/notices.

External map: https://legacy.witcher3map.com/ , untamed0 and witcher3map contributors.
The source identifies its licence as CC BY-NC-SA and game artwork/text as CD PROJEKT RED property.
The 2026-10-29 asset round imports five publisher-art regional backgrounds under the owner's
attributed-use policy. [Asset provenance](ReferencePacks/assets.source.md) and its audit record
the pinned map repository, TMS assembly research and input/output hashes. The map host's
community-work terms remain CC BY-NC-SA. No community coordinates, marker names/descriptions
or code were copied. Native saved-tag region/coordinate joins remain unknown, so regional maps
are separate reference artwork with no saved overlay. The independent saved-tag list and external
guide remain available. The plugin makes no external requests; F9 shows full art attribution.

Refill's two exact ordinary native IDs (`Bread`, `Bottled water`) are independently observed in
the supplied saves. They are an original conservative allowlist, not an imported item-cap dataset.
The existing item reference pack provides attributed names/descriptions separately. Quest variants,
tagged/equipped/slotted records and other IDs are excluded; the ceiling is the original document's
quantity, not a claimed legal maximum.
