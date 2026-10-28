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
