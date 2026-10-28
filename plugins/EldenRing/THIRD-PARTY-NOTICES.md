# Reference implementation notice

Elden Ring format facts, item-map traversal, bounded inventory/equipment reading and the
read-only regulation AES/DCX/BND/PARAM-directory reader and equipment relationship checks are based on ClayAmore/ER-Save-Editor,
revision `014107f0ca1cff867b5f7565cb79d6684f9b35df` (retrieved 2026-10-28):
https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df

The adaptation adds bounds, output budgets and snapshot-specific BND flag/PARAM directory checks;
it excludes unsafe PARAM field casting, restricted/unlicensed databases, descriptions and game artwork.
That project offers MIT and Apache-2.0 licences; the MIT option is used for this adaptation.

The complete literal name/ID pairs from `src/db/weapon_name.rs`, `item_name.rs`, `armor_name.rs`,
`accessory_name.rs` and `aow_name.rs` at the same revision are redistributed under MIT in
`ReferencePacks/eldenring.names.en`. Names are unchanged; IDs are category-qualified; empty/test/
sentinel rows are preserved with explicit metadata. Each table has its own source/hash sidecar.
The full MIT notice below also accompanies the embedded data as `LICENSE-MIT.txt`.

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
