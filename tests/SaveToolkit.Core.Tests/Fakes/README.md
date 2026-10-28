# Items reference-picker test plugin

`ItemsPlugin` is a synthetic save format used by the F2/F4 framework tests. It ships the embedded
`ReferencePacks/test.items.en` pack and uses `ReferenceSpec("items")` for item IDs in table cells and
an action parameter. The slot policy rejects equipment with an eligibility reason; unknown current
IDs remain intact and can be replaced with a known consumable. Item replacement rebuilds the view
because names and quantity limits depend on the ID. No real save data or game assets are included.

## Sources and attribution

| Dataset | Source | Author | Licence | Retrieved | Modifications |
| --- | --- | --- | --- | --- | --- |
| `items` | [Toolkit repository](https://github.com/JKamsker/PS5SaveGame-Toolkit) | PS5 Save Game Toolkit contributors | [Original test fixture, no third-party content](https://github.com/JKamsker/PS5SaveGame-Toolkit) | 2026-10-28 | None; written for framework tests. |
| `icons` | [Toolkit repository](https://github.com/JKamsker/PS5SaveGame-Toolkit) | PS5 Save Game Toolkit contributors | [Original test fixture, no third-party content](https://github.com/JKamsker/PS5SaveGame-Toolkit) | 2026-10-28 | None; generated 16×16 placeholder squares. |

The pack manifest contains the same attribution beside the datasets. `Info.References` names the
source repository; field descriptions, picker details and the Sources panel expose attribution in
the UI. This content is original test data, not a port or extract from a game/community catalog.

## Spatial-view test plugin (F9)

`SpatialPlugin` demonstrates a prerequisite graph, existing field/action navigation, an original
embedded F2 schematic with explicit CC0 permission, and an external-only region with no imported
coordinates or artwork. `SPAT` plus two flag bytes is synthetic data; the tests preserve it without
edits and verify marker-state refresh and undo after an edit. No real saves are included.

| Dataset | Source | Author | Licence | Retrieved | Modifications |
| --- | --- | --- | --- | --- | --- |
| `test.spatial/maps`, `schematic.png` and schematic coordinates | [Toolkit repository](https://github.com/JKamsker/PS5SaveGame-Toolkit) | PS5 Save Game Toolkit contributors | [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/) | 2026-10-28 | Original invented courtyard/cache/gate geometry and PNG, created for F9 tests; no third-party content. |
| External-only region | [Illustrative external source](https://example.org/community-map) | Example community (fictional fixture attribution) | Unverified; link only | 2026-10-28 | No imported data, artwork or coordinates; the fixture never contacts this URL. |

The original schematic has a `schematic.source.md` sidecar and a pack manifest with the same
attribution. The plugin's `Info.References`, map attribution and document Sources panel identify
the source; external links show their host and attribution. No third-party code is ported.
