# Currency config identities (#40)

Author/project: **Tkachov / Overstrike Modding Tool**. Accessed **2026-10-29**.
Source: [MT/v1.4.1 release](https://github.com/Tkachov/Overstrike/releases/tag/MT/v1.4.1),
[`hashes.zip`](https://github.com/Tkachov/Overstrike/releases/download/MT/v1.4.1/hashes.zip),
published 2025-01-30; asset updated 2025-01-31. The archive contains three text lists.
Only these two factual entries from `hashes.txt` are embedded as constants and labels
in `RiftCurrencyView.cs`; hash spellings are normalized to lowercase:

| Config path | Config token |
| --- | --- |
| `configs/pickup/pup_bolt_item.config` | `947dc517d343ad64` |
| `configs/pickup/pup_raritanium_item.config` | `95ffa4c1254a6f29` |

Archive SHA-256: `b6c72c5027249e11de162f429d89dd99d4fb41ab2bccdfa9297513952d3beeff`.
Uncompressed `hashes.txt` SHA-256:
`c295a89835dfde0605d03501adc840572419e9ded1dedb186253097e3ad51d15`.

**No licence published; reused with attribution per owner decision 2026-10-28**
applies to the release dataset, which includes no separate licence notice. The
Overstrike source repository has a [GPL-3.0 licence](https://github.com/Tkachov/Overstrike/blob/443ec468b5a7a922771dce6bfa96396f56896dcb/LICENSE.txt).
No upstream implementation, executable, full dictionary or game asset is embedded or executed.

Item schema identity/order is independently attributed to **Neptuwunium / Rivet**,
[`rcra_ddl.json` at a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json),
[CC0-1.0 file notice](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license),
rechecked 2026-10-29. The schema supplies names, not PS5 absolute offsets or game caps.

Independent PS5 corroboration: all 36 primary gameplay records and their 36 backup
twins use the existing sequential `InventoryReader` decode. Each matching item is a
unique `GameItemPrius` (`4E5CE3CB`) in container `78cb152e/0`, 23-byte payload,
presence byte `0F`, config bytes at payload +9, LE u32 ItemCount at +17. All bolt
records are present; raritanium records are absent in two primary records. Absence
stays unresolved, without inventing zero. Path hashes are reproduced by the existing
independent `RiftAssetId.Hash` implementation and covered by synthetic vectors.

Confidence is **Inferred**, read-only. No HUD, cap, accounting/dependency writer or
save-body integrity claim follows from a config identity. See
[currency research](../../CURRENCY-RESEARCH.md) and the README's negative results.
