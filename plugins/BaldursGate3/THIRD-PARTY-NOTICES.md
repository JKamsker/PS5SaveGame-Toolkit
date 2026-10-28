# Third-party notices

The package and LSF layouts, attribute type IDs, compact attribute-chain algorithm,
translation decoding, and GUID display convention were adapted from Norbyte's
LSLib (reference revision `6f5f6987d10d8485876b8368431c948e4dea4f83`).
The implementation uses checked span reads instead of upstream unsafe structure
blitting, performs no I/O, and implements a restricted read-only subset.

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
