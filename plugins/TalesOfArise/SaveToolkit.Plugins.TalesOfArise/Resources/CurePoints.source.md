# Party CP layout attribution

Facts, not a code port. Author: **Tuuuup!**. Accessed **2026-10-29**.
Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.

- Author post: https://fearlessrevolution.com/viewtopic.php?t=17367#p210160
- `Tales of Arise-v9.CT`, author update 2022-04-13 (CP script dated 2022-04-08):
  https://fearlessrevolution.com/download/file.php?id=41559
  SHA-256: `cdf199322583458a75ef9607594b17746bb81d4a017eeead7dcc595f5f256b7a`.
- Earlier `Tales of Arise.CT`, script dated 2021-09-10:
  https://fearlessrevolution.com/download/file.php?id=35223
  SHA-256: `a1dc3bd222d4bc1af47e8773962458970dcc75c02950379c6b73978e4336ccc8`.

Inspected downloaded XML and assembler **as untrusted text only**; no table, Lua,
assembler, executable or upstream script was executed. Downloaded files remain
outside the repository. No upstream implementation or substantial text is copied.

Extracted layout facts: v9 identifies unsigned 16-bit current CP at `cpoints+0x0C`,
unsigned 16-bit max CP at `+0x0E`, and unsigned 32-bit gald at `+0x18`.
The earlier table identifies 16-bit CP at `mon+0x9C` and money at `+0xA8`;
both shift by `-0x90` to the v9 offsets. These are **PC runtime** layouts.

Independent PS5 observation: in the supported PartyProfile schema, body `+0x0C`
is the packed word `0x009B009B` (155/155 as two little-endian halfwords) in all
three slots and their identical backup twins. The existing gald offset `+0x18`
agrees. Section-relative addressing, byte-identical cipher round trips and
synthetic halfword tests validate access, not gameplay semantics. There is no
controlled CP spend/refill or level-up pair, so the mapping is **Inferred** and
read-only. No growth formula, active-party identity or writer is claimed.

Implementation: independently authored halfword reads in `AriseReadOnlyViews`,
offsets/attribution in `AriseSchema`, and two read-only bindings in the Party tab.
The source is listed in plugin references and both field descriptions. No
third-party implementation/dependency was added; no central notice edit is needed.
