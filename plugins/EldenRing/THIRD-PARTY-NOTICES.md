# Reference implementation notice

Elden Ring format facts, item-map traversal, bounded inventory/equipment reading and the
read-only regulation AES/DCX/BND/PARAM-directory reader and equipment relationship checks are based on ClayAmore/ER-Save-Editor,
revision `014107f0ca1cff867b5f7565cb79d6684f9b35df` (retrieved 2026-10-28):
https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df

The adaptation adds bounds, output budgets and snapshot-specific BND flag/PARAM directory checks;
it excludes unsafe PARAM field casting, explicitly restricted databases, game descriptions and artwork.
That project offers MIT and Apache-2.0 licences; the MIT option is used for this adaptation.

The write-enable round also uses the same `src/save/common/save_slot.rs` and PlayStation
`src/save/playstation/user_data_10.rs` layout facts for original bounded, in-place name/history/quantity
writers. It adds strict Unicode validation, preservation of each original name tail, both history
mirrors, conservative sample-observed ceilings, atomic rollback and shared F6 refill/linked previews.
These modifications do not copy gameplay PARAM tables or claim console acceptance/legal caps.

The complete literal name/ID pairs from `src/db/weapon_name.rs`, `item_name.rs`, `armor_name.rs`,
`accessory_name.rs` and `aow_name.rs` at the same revision are redistributed under MIT in
`ReferencePacks/eldenring.names.en`. Names are unchanged; IDs are category-qualified; empty/test/
sentinel rows are preserved with explicit metadata. Each table has its own source/hash sidecar.
The full MIT notice below also accompanies the embedded data as `LICENSE-MIT.txt`.

The completion round also redistributes the same revision's `classes.rs`, `graces.rs`,
`map_name.rs`, `bosses.rs`, `maps.rs` and selected `event_flags.rs` lookup tuples through
`ReferencePacks/eldenring.progress.en` (499 progress candidates and 10 class records).
The explicit sparse joins/LSB read follow `src/vm/events.rs` and `src/util/bit.rs`, also MIT.
Source hashes and exact modifications are in the adjacent `progress.source.md`; original labels
are retained in `upstreamName` where the framework trims display whitespace. These are read-only
Inferred candidates, not verified PS5 completion, quest prerequisites, legal minima or DLC coverage.
No upstream implementation was executed; no art/coordinates or restricted data were imported.
The acquisition skip now uses the source's four-u32 (16-byte) GaItem2 rows rather than 8 bytes.
The F9 level graph composes the existing validated attribute/level equation with field navigation;
it does not import a quest graph or invent upgrade/vital derivations. The full MIT notice also
accompanies the new pack. Map Genie is an attributed external link only; no content is redistributed.

## Unlicensed community facts (resolved 2026-10-28 owner decision)

`eldenring.community.en` imports TGA `393c439e72ad48128efdc5e30fc3feb4bfbd210f`, Paramdex
`ff7245e524329bc3eab00036723d2bd53384cedf` and the ER-Save-Lib event block map at
`664ff9e83b7022a6fdc5a8332e8b7b22dce34e0f`. No root licences were found. Each dataset says:
**No licence published; reused with attribution per owner decision 2026-10-28**.
This is the owner's reuse authorization, not an assertion that upstream granted a licence.

TGA contributors (Dasaav/Umgak flag data; Apricus, Mar_Veloz and other item-group contributors)
provide 3,221 item identities/groups and 1,748 base/DLC/NG+ flag observations. ClayAmore's
MIT `items.rs` at the existing editor revision explicitly inherits TGA: 19 groups / 545 active
occurrences are now imported with both sources credited; no blanket MIT relicensing of TGA data.
The ER-Save-Lib map supplies sparse block facts. Original bounded C# reading composes these facts;
no unlicensed implementation, Cheat Engine script, predicates, runtime pointers or writer is copied.

Paramdex contributors provide 1,072 technical layout records across ten definitions. The v1.0.0
standalone dump of 8,651 row labels was removed in v1.1.0 because no native save-data view or
decoder consumed it. Only declaration/type/version/order facts are extracted.
Game XML descriptions/display text, defaults/editor bounds, game artwork and full schema files
are excluded. No item caps or field compatibility are inferred from a schema or version match.

Original source-byte/pack hashes, authors, URLs, retrieval date (2026-10-29), exact transformations
and reproducible importer accompany the data in `community.source.md`. Dataset licences are separate
from the MIT notice below. The README, `Info.References`, F2 Sources, fields and reference browsers
retain provenance. Explicit NC/no-redistribution sources, including er-save-manager, remain excluded;
game-extracted descriptions and artwork remain excluded as publisher assets.

In v1.1.0 the JSON is minified. Dataset-wide source URLs, revisions, hashes, schema and uncertainty
live in attribution/source sidecars; table layout metadata lives once per summary. Per-record source
overrides are retained where different. This storage change does not change source ownership or terms.

MIT License

Copyright (c) 2024 ClayAmore

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
