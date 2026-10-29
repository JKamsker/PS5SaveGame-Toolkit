# Malformed-input correctness review (#40)

Reviewed 2026-10-29 on `review/robust-baldursgate3`, starting at `f980f8a`,
which already includes the merged performance review. Only `plugins/BaldursGate3/`
was changed. This is a correctness/robustness review.

## Findings and fixes

| Severity | Finding | Fix commits |
| --- | --- | --- |
| medium | Tiny compressed inputs with corrupt, in-budget expanded sizes or file-table counts allocated 16–18 MiB before rejection. | `169b115`: preflight raw LZ4 lengths/references without output allocation; count streaming output with bounded scratch before allocating its final buffer. `f44e878`: also enforce the LZ4 terminal-literal and last-match restrictions before allocation. |
| medium | LSF expanded earlier sections before validating later physical ranges; record alignment and key record limits were enforced after expansion. | `1b78cf1`: validate every section range, raw stored-size consistency and node/attribute/key record size/count before expansion and shared-budget reservation. |
| medium | .NET zlib accepted a member truncated after its output but before its complete ending/checksum. A 322-byte vector was accepted at 317 bytes. | `b4d51b8`: a local input stream rejects exhaustion when the decoder still needs bytes. The library continues validating the checksum; valid trailing residue remains supported. No global runtime switch or replacement checksum implementation. |
| medium | LSX element/attribute limits were checked after XML materialization and during model projection. A 4 MB input with 1,000,001 elements allocated 530,776,352 managed bytes before rejection. | `4f9a975`: count elements, attributes and one nonblank direct-text value per element in the existing streaming pass; reserve document counts before materialization. |
| low | Missing framed LZ4/zstd members were accepted when their declared output was zero. | `5988258`: reject empty compressed input while retaining proper empty frames and supporting the raw LZ4 zero-literal token. |

All findings were posted on #40 before their fixes. No confirmed findings are
deferred. No high-severity findings were identified.

## Regression evidence

- Baseline complete Release plugin suite: **247 passed / 0 failed / 0 skipped**.
- Initial allocation/metadata regression run: **15 failed / 1 passed**, including
  four inflated codec sizes, three package table counts, four malformed LSF table
  sizes and a later-section allocation check. Codec prefix cases also exposed
  inconsistent internal codec exceptions before normalization.
- After codec normalization, the zlib prefix case still failed because truncated
  data was accepted. It passes after the local exhaustion adapter.
- LSX excessive-element allocation test failed at **530,776,352 managed bytes**;
  both element and attribute excess now fail within a **1 MiB** rejection budget.
- Two missing zero-output member cases failed before the guard. Proper empty
  raw/framed LZ4 and zstd members, a known empty zlib member, zlib checksum corruption
  and zlib trailing-residue acceptance are covered separately.
- A token-consistent LZ4 block with an invalid ending allocated **16,779,440 bytes**
  before the terminal-sequence follow-up; it now fails within **1 MiB**. A separate
  final-match-distance vector retains a clear diagnostic.
- Exhaustive incomplete synthetic byte prefixes cover LSPK v15/v16/v18 with all
  four codecs, LSF v5/v6/v7 compact/adjacency layouts and raw/framed LZ4/zstd/zlib,
  UTF-8/UTF-16 LSX, and every required LSMF directory byte. Optional LSMF residue
  remains preserved.
- Package mutations cover corrupt stored/expanded sizes, counts, flags, parts,
  offsets and UTF-8 names. Payload truncation retains a structurally valid package
  table and original expanded-content digest, ensuring the codec performs rejection.
- Four sample-backed cases mutate in-memory copies of the real package, meta/Globals
  LSF and NewAge directories. Original sample files are never modified or committed.
  Valid controls retain exact no-op bytes.

Final complete Release suite: **306 passed / 0 failed / 0 skipped**: **59 added
cases**, including **55 synthetic** and **4 sample-backed** cases. API Release build
with `--warnaserror`: **0 warnings / 0 errors**. Existing conformance, sample census,
edits/reopen, history, byte preservation, warning and allocation tests pass.
All **55 new synthetic cases** also pass with `SAVETOOLKIT_ARTIFACTS` unset:
**55 passed / 0 failed / 0 skipped**.

## Limits and implementation choices

The documented rejection remains `SaveFormatException`; codec truncation/error
details are wrapped consistently. Resource models remain lazy in the UI. Optional
unknown NewAge variants and opaque Story payloads retain their existing documented
inspection/preservation behavior; these are not new format rejections.

Raw LZ4 preflight scans compressed tokens without expanding data. Other codecs
perform a validation pass using 16 KiB output scratch plus their codec state,
then decode into the retained output buffer. This adds a decoding pass for zlib,
framed LZ4 and zstd. Package validation payload reuse, independent Extract arrays
and the performance review's managed-allocation tests remain intact. No new
latency guarantee is claimed.

Existing 256 MiB section/document and one-million record budgets remain. zstd
decoder windows are explicitly capped at 256 MiB; codec state is additional to
output scratch. Allocation assertions measure per-thread **managed** allocation,
not native codec memory or process working set. They prove corrupt declarations
do not directly allocate their large managed output/table arrays. The codec
window ceiling separately bounds its internal window requirement.

Experimental maturity, write warnings, integrity validation and disabled writers
remain unchanged. Source research and attribution are in the README's Sources
table; no new upstream parser implementation was copied or executed.

## Reproduce and retained evidence

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/review-robust-baldursgate3'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = $scratch
$env:TMP = $scratch
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests -c Release --report-xunit-trx --report-xunit-trx-filename full-suite.trx --results-directory $scratch
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
```

TRX/log evidence is retained at
`D:/Temp/saveeditor/reviews/review-robust-baldursgate3/`, including baseline,
red/green focused runs, final suite and sample-independent validation.
Source files remain below 300 physical lines. No outside-scope files, samples,
hooks, build targets or dependencies were added. No push, merge or PR.

Cleanup handoff: automatic approval review rejected recursive deletion of
`D:/Temp/saveeditor/test-scratch/review-robust-baldursgate3` with **blocked by policy**.
No alternative deletion was attempted; the task scratch remains for the orchestrator.
The separate retained-evidence directory above is complete.
