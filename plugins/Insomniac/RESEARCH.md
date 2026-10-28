# Round-two evidence (2026-10-28)

Historical round-two report. Its ReadOnly decision is superseded by the owner's
write-enable decision on the same date: 0.5.0 enables bounded scalar writes as
Experimental with explicit unknown-integrity / game-may-reject warnings. The
technical observations below still hold; see README and issue #31 for current
scope and console validation. No checksum or acceptance evidence was invented.

These are observations of the supplied decrypted PPSA01474 archive, not an in-game
validation. Source URLs and licensing are collected in [README.md](README.md).

## Lead outcomes

The NinjaSaves listing names career bolts/XP, weapon XP, multiplier, challenge availability
and seven gold-bolt options. No patch payload is exposed on that page. Searching
PPSA01474, Rift Apart, Ratchet + save/code/checksum/DDL, the SaveWizard-Resources code
and Apollo-patch trees, and the Apollo database did not locate a PS5 Rift Apart save
code. No code format, absolute-write target, search anchor or checksum-fix opcode could
therefore be decoded. Memory-trainer listings and older PS3/PS4 Ratchet patches were
not treated as PS5 save-write evidence.

The productive lead was Rivet's actual save reader and DDL schema. Its `Checkpoint`
section matches the PS5 tag using the engine's unusual seeded CRC32. All campaign,
gold-bolt and speedrun root field names in all 72 gameplay copies pass that name/hash
check. This establishes names independently of numeric plausibility.

## Representative bytes and changing widths

Paths below are relative to `PPSA01474/`; all are `i29-s.save`. Generation 1 means the
second occurrence of a repeated path in the original zip. Offsets are absolute within
that specific record and must **never** be reused as fixed patch addresses.

| Field | `sdimg_i29slot0-s-autosave`, generation 0 | Same path, generation 1 |
| --- | --- | --- |
| `CareerBolts` | u32 at `0xB214` = 977095 | u16 at `0x5F4F` = 23444 |
| `SpentBolts` | u32 at `0xB218` = 260875 | u16 at `0x5F4D` = 14858 |
| `CareerExp` | u32 at `0xB21C` = 2100000 | u32 at `0x5F45` = 69297 |
| `CareerWeaponExp` | u32 at `0xB220` = 23017327 | u32 at `0x5F49` = 444364 |
| `CareerRaritanium` | f32 at `0xB224` = 531.1996459960938 | u8 at `0x5F54` = 40 |
| `CareerRaritaniumSpend` | u16 at `0xB23A` = 584 | u8 at `0x5F58` = 20 |
| `Playtime` | f32 at `0xB228` = 46376.29296875 | f32 at `0x5F31` = 7053.69091796875 |
| `RYNOPlansCollected` | u16 at `0xB238` = 553 (`0x229`) | u8 at `0x5F51` = 0 |
| `Difficulty` | u8 at `0xB248` = 1 | u8 at `0x5F5B` = 0 |
| `IsChallengeMode` | bool at `0xB240` = false | bool at `0x5F67` = false |
| `CollectedBoltHashes` | u32[7] at `0xB388` | null/empty at `0x60E0`, count 0 |

In particular, the early field order differs from the late order. Assuming the PC
schema's 64-bit XP, double playtime or float raritanium would silently misread PS5
values. Assuming absolute string alignment also shifts the late counters by two bytes;
the resulting implausible values were rejected during research. The committed parser
uses each actual descriptor and the sample-confirmed string padding.

The career/spent bolt difference is 716220 in the late autosave and 8586 in the early
autosave. These are inferred available balances. Raritanium cannot safely be treated
the same way: late values imply a negative difference, and the fractional increments
may involve gameplay bonuses or accounting rules that have not been established.

## All 36 primary gameplay records

The 38 backup twins (including preferences) are byte-identical to their corresponding
primary occurrence. Snapshot numbers below abbreviate `sdimg_i29slot0-s-snapshot-N`;
`auto` means slot0 autosave. Tables order records by stored `Playtime`; that ordering
is a consistency check, not an independent calibration of time or save chronology.
Times and fractional raritanium are rounded here; tests retain exact float values.

Campaign A has `CampaignSaveID = 718673647`, 19 records, difficulty code 1, seven gold
bolts. Career XP ranges from 1586615 to 2100000; aggregate weapon XP ranges from
10785239 to 23017327 in these records.

| Snapshot / slot | Playtime | Career bolts | Spent bolts | Career raritanium | Raritanium spent | RYNO mask |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 108976056 | 38983.379 | 363925 | 226875 | 306.800 | 304 | 545 |
| 505121150 | 39456.285 | 380515 | 226875 | 306.800 | 304 | 545 |
| 2005770781 | 39532.797 | 380515 | 226875 | 306.800 | 304 | 545 |
| 1747363763 | 39756.020 | 397073 | 226875 | 306.800 | 304 | 545 |
| 522295077 | 39966.031 | 425362 | 226875 | 306.800 | 304 | 545 |
| 1984049680 | 40189.801 | 451782 | 226875 | 306.800 | 304 | 545 |
| 104506980 | 40477.109 | 457486 | 226875 | 306.800 | 304 | 553 |
| 1867211690 | 40819.910 | 477161 | 226875 | 328.850 | 304 | 553 |
| 1529440392 | 45195.137 | 952841 | 260875 | 531.200 | 584 | 553 |
| 381373439 | 45196.820 | 952841 | 260875 | 531.200 | 584 | 553 |
| 1177913416 | 45368.109 | 962164 | 260875 | 531.200 | 584 | 553 |
| 1623926022 | 45405.281 | 962164 | 260875 | 531.200 | 584 | 553 |
| 1777266847 | 45436.609 | 962164 | 260875 | 531.200 | 584 | 553 |
| 867870254 | 45806.773 | 966960 | 260875 | 531.200 | 584 | 553 |
| 660216862 | 46025.246 | 974096 | 260875 | 531.200 | 584 | 553 |
| auto, generation 0 | 46376.293 | 977095 | 260875 | 531.200 | 584 | 553 |
| 0 | 46376.293 | 977095 | 260875 | 531.200 | 584 | 553 |
| slot1 snapshot 412553368 | 46387.184 | 1297735 | 260875 | 531.200 | 584 | 553 |
| slot1 autosave | 46398.027 | 1297735 | 260875 | 531.200 | 584 | 553 |

The two slot1 records have `IsChallengeMode = true`; earlier slot0 records have it
false. The shared campaign ID and resource jump are consistent with a challenge-mode
copy, but the reason for the bolt jump was not independently verified.

Campaign B has `CampaignSaveID = 2112248476`, 17 records, difficulty code 0, zero gold
bolts. Career XP ranges from 17150 to 69297 and aggregate weapon XP from 67362 to 444364.

| Snapshot / slot | Playtime | Career bolts | Spent bolts | Career raritanium | Raritanium spent | RYNO mask |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1802099279 | 1804.243 | 4713 | 2500 | 0 | 0 | 0 |
| 1000115474 | 1990.295 | 5367 | 2500 | 5 | 0 | 0 |
| 1412875998 | 2352.962 | 5698 | 2500 | 5 | 0 | 0 |
| 1813725300 | 2684.817 | 10464 | 2500 | 7 | 0 | 0 |
| 723348676 | 2763.445 | 10564 | 2500 | 7 | 0 | 0 |
| 353949189 | 3216.198 | 11013 | 7500 | 10 | 10 | 0 |
| 2127947730 | 3671.436 | 12913 | 7500 | 15 | 10 | 0 |
| 1992350878 (alternate branch) | 3839.539 | 8218 | 5522 | 10 | 0 | 1 |
| 379696474 | 4185.532 | 13669 | 7500 | 20 | 10 | 0 |
| 1587540500 | 4482.180 | 16907 | 7500 | 22 | 10 | 0 |
| 536658292 | 4999.760 | 18628 | 10514 | 26 | 20 | 0 |
| 256473048 | 5082.542 | 18840 | 10514 | 26 | 20 | 0 |
| 1106495269 | 5260.136 | 19472 | 10514 | 26 | 20 | 0 |
| 1099990005 | 6430.023 | 21717 | 14550 | 31 | 20 | 0 |
| 339046575 | 6652.085 | 22763 | 14550 | 34 | 20 | 0 |
| 1506250198 | 6977.289 | 23444 | 14550 | 39 | 20 | 0 |
| auto, generation 1 | 7053.691 | 23444 | 14858 | 40 | 20 | 0 |

Removing only the identified alternate branch, all six career/spending counters are
nondecreasing in both groups. Including it reveals declines in career bolts, spent
bolts, both XP counters and both raritanium counters; the spybot bit then disappears
from the following record. This is consistent with replay/rollback, not a reason to
reinterpret the substantiated fields. Available bolts vary after purchases, as expected.
The spending increase from 14550 to 14858 with career bolts unchanged is one example.

Gold counts agree with the sum of `LevelGoldBolts` in every primary record, and the
RYNO mask population count agrees with the sum of `LevelRYNOPlans`. Masks 545
and 553 differ by bit 3, giving inferred counts three and four. No collectible-location
dictionary, difficulty enum, player-health threshold or individual-weapon schema was
established. Do not turn aggregate XP or location arrays into guessed weapon levels.

## Integrity experiment

`research/inspect_integrity.py` reads original `ZipInfo` entries, retaining duplicates
and skipping backup twins. It writes nothing to the repository. Re-run using the
command in README. Hash implementations first check known vectors, including CRC32,
CRC32C, three CRC64 variants, FNV-1a and Murmur3.

For every one of the 19 sections in each of the 36 primary gameplay records:

1. Hash the entire payload, skip its first 4/8 bytes, or exclude its final 4/8 bytes.
   Impossible/empty coverages are skipped, including in the one-byte section.
2. Compute CRC32 ISO-HDLC/JAMCRC/engine-name-seed, CRC32C, CRC64 ECMA-182/XZ/GO-ISO,
   FNV-1 and FNV-1a 32/64, and Murmur3 x86_32 with seeds 0 and `0xFFFFFFFF`.
3. Search both byte orders anywhere else in the file, including unaligned locations.
   A result must lie outside the hashed range; self-covering checksum/residue schemes
   are not tested. Ignore degenerate zero/all-one values rather than claiming hits
   in ubiquitous padding.

Result: **41,652 digest candidates**, **161 degenerate candidates ignored**, **zero
remaining match locations**. Every tag had 36 records checked. This is finite negative
evidence; it does not exclude unknown seeds, nonstandard ranges or algorithms.

Two close-save comparisons provide additional, limited evidence:

* Speedrun section in snapshots 1747363763 and 522295077: five differing bytes at
  relative offsets `0x29..0x2D`, all within the double at `0x28..0x2F`.
  `SpeedrunTime` changes from 39083.53884002473 to 39288.681829200126; header and
  `TimerStarted` are unchanged. No independent checksum word changes in that section.
* Campaign section in snapshots 1623926022 and 1777266847: 14 changed bytes. They
  belong to `CurrentCheckpointId`, float32 `Playtime` (45405.28125 → 45436.609375)
  and `CurrentCheckpoint`. All other root fields remain equal. This does not test
  whether a checksum in another section covers these values.

The eight-byte `0xBC60B495` section is not a promising generic digest: interpreted as
u64 LE, it **exactly equals `CareerExp` in all 36 primary records** (2100000 in the
late autosave), including its zero high word. This empirical mirror is covered by
tests but is not treated as a complete integrity scheme.

DDL string records also contain hashes, and field names have validated CRC identifiers.
Their presence must not be confused with a checksum over mutable gameplay values.
No confirmed section-body checksum was identified or recomputed.

## Decision and remaining blockers

Keep **ReadOnly**. Field identification advanced substantially, but the required
plain-write evidence is unavailable and a complete integrity specification remains
unknown. The parser rejecting malformed framing/names/types does not certify arbitrary
changes to unknown payloads. Next useful input is a controlled console-validated edit
pair or a documented PS5 patch that includes any hidden postprocessing. Inventory
`INVT` decoding is separate remaining work for health, armor and weapon ammunition/XP.
