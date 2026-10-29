# Third-party notices

The 2026-10-29 gold/base-score discovery additions independently interpret measured
PS5 storage using existing plugin primitives; no new upstream code or dataset is
incorporated. [FIELD_DISCOVERY.md](FIELD_DISCOVERY.md) records factual research
sources, revisions and access dates. Norbyte/bg3se's MIT plus Commons Clause v1.0
component code was read only for facts and excluded from reuse; see the README's
Licence exclusions. No legacy NC wiki prose is included.

The package and LSF layouts, attribute type IDs, compact attribute-chain algorithm,
translation decoding, GUID display convention, MD5+1 algorithm, compression-level
mappings and writer section/padding rules were adapted from Norbyte's
LSLib (reference revision `6f5f6987d10d8485876b8368431c948e4dea4f83`).
The implementation uses checked span reads instead of upstream unsafe structure
blitting, performs no I/O, and implements a restricted subset with preservation-first
primitive writers. Community project documentation informed the read-only Party
summary; no community parser/editor implementation was copied.

LSLib: <https://github.com/Norbyte/lslib>

The MIT License (MIT)

Copyright (c) 2015 Norbyte

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

## NuGet dependencies

These packages are referenced, rather than copied into the plugin. Their packages
carry their own notices and transitive dependency licences:

- `K4os.Compression.LZ4` and `K4os.Compression.LZ4.Streams` 1.3.8, MIT:
  <https://github.com/MiloszKrajewski/K4os.Compression.LZ4/blob/master/LICENSE>.
- `ZstdSharp.Port` 0.8.8, MIT:
  <https://github.com/oleg-st/ZstdSharp/blob/master/LICENSE>.

No community editor source was copied or executed.

## Starter reference data (separate from code)

`ReferencePacks/bg3.starter.en/*.json` contains factual labels, categories and
technical identifiers selected from pinned bg3.wiki revisions and original
annotations by PS5 Save Game Toolkit contributors. Retrieved 2026-10-28. This data adaptation is licensed
under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/); retain its
attribution, linked sources, modification notice and share-alike terms.
The complete dataset/source/modification record, including every pinned `oldid`, is
beside the embedded data in `data.source.md` and `pack.json`, and in the README's
Sources and attribution table.
No wiki editorial prose or biographies are included. The wiki's
[copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights) distinguishes legacy
CC BY-NC-SA expressive content; no blanket redistribution right to that content
is claimed. Game assets have the separate attribution below.

LSMF research consulted DanFinchUK/bg3-savefile-parser's pinned format document
(`9578ff7c46a1aa40c805f6b7beecf907f10fb3b8`). No licence published; reused with
attribution per owner decision 2026-10-28. The reader reimplements documented and
independently measured format facts; no repository code was copied or executed.
See `LSMF.md` and `NEWAGE.md` for scope, joins and validation.

## Larian game icons and text excerpts (asset round 2026-10-29)

Rights reserved by Larian Studios; used with attribution per owner decision 2026-10-29.
Retrieved from bg3.wiki on 2026-10-29: 51 compact game icons and 39 distinct short
in-game tooltip/flavour-text excerpts. These assets retain their rights-holder notice;
they are separate from GPL code and the CC BY-SA factual-label adaptation.
See [asset licensing](https://bg3.wiki/wiki/Help:Licenses),
[wiki copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights), and
[Larian fan-content policy](https://larian.com/fan-content-policy).
Exact page/file revisions, original image hashes, processing and per-file byte counts
are in `ReferencePacks/bg3.starter.en/assets.source.md`; manifest hashes cover the
processed embedded files. Header verification, 64px resizing, WebP/PNG-8 conversion,
image deduplication and tooltip markup/spacing normalisation were performed offline.
Long descriptions are marked excerpts. No legacy CC BY-NC-SA wiki editorial prose
was imported; that exclusion remains in the README.

The `bg3_classes` dataset selects 12 UUID/name facts from DanFinchUK's
[gamedata.json at 9578ff7c46a1aa40c805f6b7beecf907f10fb3b8](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/data/gamedata.json).
No licence published; reused with attribution per owner decision 2026-10-28.
No upstream implementation was copied or run; badges use the Larian notice above.
