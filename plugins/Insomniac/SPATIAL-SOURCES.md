# Spatial presentation provenance (0.8.0)

## Community regional coordinates

[Guides4Gamers, Rift Apart maps](https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/)
is the sole community source previously refused only for licence status in this
plugin's README, notices, sidecars, evidence, research and completion notes.
The source footer states "All rights reserved."

**Resolved: imported per owner decision 2026-10-28.**
Licence metadata: **No licence published; reused with attribution per owner decision 2026-10-28**.
This describes the owner instruction, not an upstream licence grant.

The `insomniac.rift.maps.en` F2 pack includes **403 factual markers across nine
regions**, retrieved 2026-10-28 UTC. Each retains source IDs, labels, categories,
map pixel coordinates/dimensions, version, URL and raw-response SHA-256.
The full per-region pins, hashes, extraction method and source links are in
[markers.source.md](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.maps.en/markers.source.md).
Manifest attribution, Info.References, per-marker descriptions/links, region
sections and F9 coordinate attribution retain the source. Sources resolves the pack.

F9 maps expand lazily by region, use explicit identity transforms and source
bounds, and now resolve nine local F2 backgrounds with the same full native extent. Native coordinates are guide-map pixels:
origin top-left, X rightwards, Y downwards. No PS5 fog/world transform is claimed.
All marker states remain **Unknown**, with Inferred coordinate confidence.
Guide marker IDs have no proven join to save masks/hashes. Source status flags
are not collection progress. Repeated labels keep distinct upstream identities.
Guide marker counts do not claim one marker per collectible or completeness.

Publisher map art is imported per owner decision 2026-10-29, with separate
rights-reserved attribution and pins in [backgrounds.source.md](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.maps.en/backgrounds.source.md).
No HTML descriptions, paths, quests, remote image requests or source implementation are
embedded. The source renderer was read to confirm coordinate conventions, not
executed or copied. The plugin performs no network or filesystem I/O.

## Save storage diagrams

The separate spybot storage map continues to use the existing `rift-world`
reference dataset (CC0-1.0, pinned Neptuwunium/Rivet schema; see its data.source.md
and pack.json). Marker IDs are actual stored bit identities. All 64 remain
Unknown; linked scalar fields expose exact set/clear storage, including reserved
bits. These storage markers have no coordinates and only link to the guide index.

Graphs are original toolkit presentations of sixteen measured ModGrid row masks
per supported fixed-mask weapon record. Schema field names derive from the same
pinned CC0 schema. Nodes have Unknown gameplay states, with no costs or
prerequisite edges. Browser layout is schematic, not an in-game upgrade-grid
transform. Scalar masks and field targets supply the text alternative.

The complete sample suite checks all 76 no-op game records; new Core integration
checks all nine guide maps against all 36 primary gameplay records, exact copied
coordinates/categories/IDs, bounds, JSON, attribution and unchanged serialized bytes.
Physical save-to-guide joins, node dependencies/costs and fog transforms still
require controlled experiments in OWNER-CAPTURES.md and owner issue #31.
