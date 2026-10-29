# PS5 Save Game Toolkit user guide

Use the toolkit to inspect and edit a **decrypted PS5 save dump** in your browser. The
browser interface is branded **Savebench**. The toolkit runs on your computer; it does
not decrypt or re-sign console save containers. Use your save manager for those steps
and for restoring the exported save to your console.

**Edits are experimental and not console-validated.** A successful edit, integrity check
or export does not prove the game will accept it. The game may reject, normalize or
overwrite changes. Keep an untouched backup outside the toolkit's folder and try small
changes on a disposable spare slot first.

- [Download and verify](#download-and-verify)
- [Extract and start](#extract-and-start)
- [Where your data lives](#where-your-data-lives)
- [Import and choose a document](#import-and-choose-a-document)
- [Browse, search and edit](#browse-search-and-edit)
- [Preview actions and review changes](#preview-actions-and-review-changes)
- [Undo and redo](#undo-and-redo)
- [Export your save](#export-your-save)
- [Confidence labels and warnings](#confidence-labels-and-warnings)
- [Supported titles and write limits](#supported-titles-and-write-limits)
- [Troubleshooting](#troubleshooting)
- [Report a problem](#report-a-problem)

## Download and verify

Open [GitHub Releases](https://github.com/JKamsker/PS5SaveGame-Toolkit/releases) and choose
a release. Repository access is required while the project is private. Download a
**release asset**, rather than GitHub's automatic source-code archive:

| Your computer | Bundle name ends with |
| --- | --- |
| Windows, Intel/AMD 64-bit | `win-x64.zip` |
| Linux, Intel/AMD 64-bit | `linux-x64.tar.gz` |
| macOS, Apple silicon | `osx-arm64.tar.gz` |
| macOS, Intel | `osx-x64.tar.gz` |

Also download the archive's matching `.sha256` file, or the release's combined
`SHA256SUMS`. Keep them beside the archive. Checksums detect an incomplete or changed
download; compare with the files from the same GitHub release.

On **Windows**, open PowerShell in your download directory. Replace `VERSION` below
with the version in the downloaded filename, including any prerelease suffix:

```powershell
$archive = 'SaveToolkit-VERSION-win-x64.zip'
$expected = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actual -ine $expected) { throw 'Checksum mismatch. Download the archive again.' }
'Checksum matches.'
```

If using `SHA256SUMS` instead, run `Get-FileHash -LiteralPath $archive -Algorithm SHA256`
and compare its hash with the **first column of the line naming your exact archive**.
Uppercase and lowercase hexadecimal letters are equivalent.

On **Linux**, in the download directory, substitute your actual filename:

```sh
sha256sum --check SaveToolkit-VERSION-linux-x64.tar.gz.sha256
```

For the combined manifest, this checks the archives you downloaded and skips the other
platforms' missing archives:

```sh
sha256sum --ignore-missing --check SHA256SUMS
```

On **macOS**, use the built-in `shasum` and your chosen architecture:

```sh
shasum -a 256 --check SaveToolkit-VERSION-osx-arm64.tar.gz.sha256
```

With all release archives downloaded, `sha256sum --check SHA256SUMS` on Linux or
`shasum -a 256 --check SHA256SUMS` on macOS checks the whole manifest. Missing archives
otherwise produce missing-file errors. Require `OK` for your archive. If its checksum
fails, download it again before extracting or running it.

## Extract and start

Extract the **whole archive** to a writable folder of your own. It contains a top-level
`SaveToolkit-<version>-<runtime>` folder. Open that inner folder, where `README.txt`,
`appsettings.json`, the launcher and `wwwroot/` live. Keep these files together. No .NET
or Node.js installation is needed, though your OS must support the bundled .NET runtime
and its native dependencies (including Linux ICU/OpenSSL).

On **Windows**, use **Extract All**, then double-click `start.cmd`. From PowerShell
in that same extracted folder, the equivalent is:

```powershell
.\start.cmd
```

On **macOS/Linux**, extract with your archive tool, or run this from the download
directory with the actual filename (use your macOS filename on a Mac):

```sh
tar -xzf SaveToolkit-VERSION-linux-x64.tar.gz
cd SaveToolkit-VERSION-linux-x64
chmod +x start.sh SaveToolkit.Api
./start.sh
```

Leave the terminal running and open **http://127.0.0.1:5180** in your browser. The home
page footer shows the running version. Export anything you want to keep, then press
**Ctrl+C** in the server terminal to stop it. The server has no authentication; keep
the address on `127.0.0.1` for use on your own computer.

### Use another port

The launchers always use port 5180 and do **not** forward extra arguments. To use
another port, stop the server and run its executable directly **from the extracted
bundle folder**. These examples use 5181; replace it with an available port:

```powershell
.\SaveToolkit.Api.exe --urls http://127.0.0.1:5181 --contentRoot . `
  --Toolkit:DataRoot ./data/workspaces --Toolkit:PackRoot ./data/reference-packs
```

```sh
./SaveToolkit.Api --urls http://127.0.0.1:5181 --contentRoot . \
  --Toolkit:DataRoot ./data/workspaces --Toolkit:PackRoot ./data/reference-packs
```

Then open **http://127.0.0.1:5181**. In general the address option is
`--urls http://127.0.0.1:<port>`. `appsettings.json` sets `Urls`, so setting
`ASPNETCORE_URLS` alone is ignored. The commands above also retain the launchers'
local data paths; running only the executable without those data options uses different
default storage locations.

## Where your data lives

When started with the launcher or the commands above, working data lives under **`./data`**
beside the launcher: imported workspace files in `data/workspaces/` and reference-pack
storage in `data/reference-packs/`. Choose a writable extraction folder with enough space
for your dump. Imports use working copies; editing does not overwrite your original zip.
Exports are downloaded through your browser to the location you choose.
Allow additional free space for expanded imports and downloaded exports; a compressed dump's size
does not indicate its working-space requirement. Release extraction, working data and backups each
need space. Contributors running tests/builds should use the separate
[disk and scratch guidance](RELEASING.md#disk-and-scratch).

**A workspace is temporary.** Edits and undo history live in the running server session.
Closing a dump discards its workspace; restarting the server clears imported workspaces,
and workspaces idle for six hours expire by default. The home page's **Open dumps** list
is for currently open workspaces, not saved editing projects. Export before closing or
stopping the app. Keep originals and exported zips outside `data/`.

## Import and choose a document

1. Make a separate backup of your original decrypted dump. Keep the original title and
   save-directory layout in its zip, for example `PPSA01325/sdimg_sce_sdmemory/memory.dat`,
   together with any `sce_sys/` metadata and console backup directories. Import the
   decrypted dump zip, not the toolkit release archive or an encrypted console container.
2. On the home page, click **Open zip**, or drop the zip on **Drop a folder or zip here**.
   Wait for import to finish. **Choose folder** is an alternative: it scans your dump
   library in the browser; open the desired game from that library.
3. In the left rail, choose a save directory/slot, then a file. Recognized files are marked
   in blue. Check the title ID, file path and slot description before editing. Gameplay,
   system and settings files can use different plugins and have different write limits.
4. The best matching plugin opens automatically. If several match, use **Open with** in
   the document header to choose another offered plugin. The raw inspector is read-only;
   generic JSON/GVAS recognition does not prove a game-specific edit is understood.

Identical console backups are hidden by default; use **Show … identical console backups**
to inspect them. **Archive copy** numbers separate repeated paths in a zip and do not
establish which save is newest. A backup marked **differs** is a distinct input, not a
copy that can safely be overwritten automatically.

Some plugins offer **Choose related files**. Review the exact members and roles before
choosing **Open group**. Start with a clean document and close other open member documents;
undo existing edits first if required. A group shares edits, undo history and export across
its listed members. Only explicitly selected backups are written as group members. Use
the offered groups only when the listed files belong to the save you intend to edit.

## Browse, search and edit

Use the plugin's tabs and expandable sections to find player values, inventory or other
supported data. Views vary by game; a reference catalogue can show items that your save
does not own. Map backgrounds, guide markers and skill diagrams may be references only.
Read their state labels and descriptions before interpreting them as saved progress.

In tables, use **Filter rows** where available, or **Search all rows** on paged tables.
Paged search/filter/sort runs over the collection, including unloaded pages. Use column
filters, **Sort rows**, **Ascending/Descending**, and **Previous/Next** when offered.
Expand a row's details for descriptions, raw IDs, confidence, warnings and edit controls.
When all currently shown field cells in a column share one confidence, its badge appears once in
that column's header; mixed-confidence fields keep their individual badges.
Some tables hide confirmed empty slots by default; use their empty-slot **Show** control
to inspect them. Showing an empty row does not enable item creation.

Check rows to select them for a table action. The page-header checkbox selects rows on
that page. **Select all … filtered rows** includes matching rows on other pages; you can
exclude individual rows afterward. Review the selected count and clear unwanted
selections before starting an action. A filter alone is not a selection.

For an editable text or number field, enter a value and press **Enter** or leave the
field to commit it; **Escape** restores an uncommitted draft. Multiline text commits when
you leave the field. Switches and ordinary dropdowns commit when changed. Reference
pickers offer searchable IDs with details; a browse-only picker cannot change a value.
Keep exact IDs and number spellings when their meaning is uncertain.

Edits take effect in the workspace as you commit them; there is no separate Save button.
Read any validation error and correct the value before continuing. A field shown as
read-only has no supported writer for that context. Numeric bounds may be storage limits
or conservative editor policies, not legal game maxima. Start with one small change and
check **Changes against original** before export.

## Preview actions and review changes

Where a plugin provides a planned action (such as setting existing stack quantities):

1. Select the intended rows, or use a document-level action. Open the action and check
   whether the dialog says **Whole document** or gives a selected-row count.
2. Enter its parameters and click **Preview**. This does not change the save.
3. Review every **Before/After** change, including **derived** changes such as linked
   counters or checksums. Page through the preview, expand **Skipped rows**, and read
   their reasons and all warnings. Empty or ineligible selections can change nothing.
4. Use **Change parameters** or **Cancel** to revise or abandon it; choose **Apply** only
   for the reviewed result. If the document or selection changed, preview again.

A planned apply normally forms one undo step, subject to the history limits below.
Some plugin actions use a simpler action/confirmation dialog; a full preview is available
only where the plugin implements it. Inspection actions browse information without edits.

Open **Changes against original** to compare exposed fields with the imported baseline.
Use **Find a changed field**, **Jump**, and **Revert** where enabled. Reverting a field
recomputes supported dependent values. Use Undo for added, removed or derived fields.
Unloaded paged-collection fields may be absent even when review reports complete coverage of its
current walk. This panel is a bounded semantic review, not a complete byte diff: heed incomplete-coverage
warnings, and do not treat **No semantic changes** as proof that every file byte is unchanged.

## Undo and redo

Use **Undo** and **Redo** in the document header. Outside text inputs, **Ctrl+Z** undoes
and **Ctrl+Shift+Z** redoes (use **Command** on macOS). Inside an input, those keys belong
to its text editing; use the header buttons to undo a committed save edit.

History belongs to the open document or related-file group. Making a new edit clears
redo. The default limit is 200 undo entries, with a 256 MiB structural-snapshot memory
budget; old history can be dropped, and an oversized change can apply without undo.
Read history warnings. Undo history is not a backup and does not survive server restart.

## Export your save

Click **Export** at the top of the workspace and choose the scope:

| Choice | What the zip contains |
| --- | --- |
| **Changed save folders** (default) | Complete affected save folders, including imported `sce_sys/` metadata. |
| **Only changed files** | Rewritten files at their imported paths; use only if you know how to combine them with the rest of your dump. |
| **Whole dump** | All imported files with edits applied; also available when nothing has changed. |

Review the file counts, paths and reasons before **Download zip**. The default option
**Also update the console backup copy (sce_bu) when it matched the original** mirrors
eligible files only when their same-generation twins held the same original bytes and
document ownership permits it. It does not reconcile differing backups. Explicit groups
manage their own selected backups.

The toolkit reopens edited output with its plugin before packing it. If verification
fails, read the error and undo or correct the edit; passing these checks still does not
prove console acceptance. Save the downloaded zip separately from the original. A
document's **Download file** downloads just that file; use workspace Export for complete
save folders and for related-file groups. Inspect the plan paths: additional generations use `genN/`,
wrapper folders above a recognized title are dropped, and an unrecognized title/layout keeps its
imported path.

You can re-import an exported zip to inspect it. Restore through your own save manager,
including any required encryption or signing. On a disposable backed-up slot, check the
displayed value, normal gameplay, an in-game save and a reload before trusting an edit.

## Confidence labels and warnings

Confidence describes the evidence for a particular field or mapping, separately from
whether the document has a writer:

| Label | How to read it |
| --- | --- |
| **Verified** | Supported by the evidence described by the plugin: this may establish a stored name, type, byte layout or checksum. It does **not** mean an edited save was tested on a console. |
| **Inferred** | A plausible interpretation or mapping from field names, samples or community sources; gameplay effects, cross-build identity or limits may still be unconfirmed. |
| **Unknown** | Meaning, mapping or dependencies have not been established. Raw values are retained; an enabled edit still carries that uncertainty. |

**Experimental** is plugin maturity, not a per-field confidence label. All current
game/format plugins are Experimental. Read-only documents and individual fields still
exist inside them. **ReadOnly** permits inspection only; no current game plugin claims
Stable, console-tested support.

Warnings can identify unknown integrity/signature coverage, unproven game dependencies,
unsupported versions, skipped rows, limited review coverage or trimmed undo history.
They explain limits even when Apply or Export is available. An integrity status of
**Verified** covers that specific implemented check, not unknown signatures or gameplay
rules. Names, images and guide locations do not prove ownership, completion or eligibility.
**Keep backups: edits remain experimental and not console-validated.**

## Supported titles and write limits

Title IDs and maturity below come from plugin registrations; write limits come from
the linked plugin READMEs. Generic JSON title overlays are listed separately from its
format-based registration. Registrations are not promises of coverage for every region,
patch or DLC. **Yes** means bounded writes in supported files/schemas, not every field.
Read each linked README for exact gates and evidence; other documents may open read-only.
The [README support table](../README.md#supported-games-and-formats) lists the same **17 game/format
plugins**, plus the built-in raw inspector; multiple rows below can belong to one plugin.

The [edit-coverage implementation status](EDIT_COVERAGE.md#implementation-status)
records all 15 merged additions, evidence and remaining owner checks as of 2026-10-29.
“Restore imported” uses this workspace's immutable opening values; it cannot recover
amounts lost before import, and re-importing an edited export establishes a new baseline.
The [wave-1 review](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489144967)
found that FF7 recovery/level, Tales restore and Tsushima restore controls ignored table
selection, and that FF7 level undo could lose the opening XP. All four fixes are merged;
still review every target in the preview before applying. The
[wave-2 review](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489356906)
found a low-severity grouped Gearbox level/mark defect with malformed manually
submitted row keys; normal UI selections were unaffected. That fix is merged too, and a
follow-up review of Pragmata found no defects.

| Game / format | Title IDs | Plugin | Maturity | Writes enabled / important limits |
| --- | --- | --- | --- | --- |
| ASTRO's PLAYROOM | PPSA01325 | [AstroPlayroom](../plugins/AstroPlayroom/README.md) `astro-playroom` | Experimental | Yes: existing coins/play count and supported flags/actions, plus restore-imported ownership previews for eligible existing IsGot fields cleared after opening. Above-baseline grants stay unchanged; no insertion, cinematic/notification/reward or complete-world reversal. Flag effects and catalogue aliases remain Inferred; special-bot insertion is unsupported. |
| Atomic Heart | PPSA10695 | [AtomicHeart](../plugins/AtomicHeart/README.md) `atomic-heart` | Experimental | Yes: existing populated Amounts, constrained same-category asset replacements and quantity previews, plus restore-imported Amounts only when lower and still eligible. Existing signed-width/container/imported ceilings remain; no empty-slot population. Inferred; stored Kraken output, growth/capacity and inner integrity are not console-validated. |
| Baldur's Gate 3 | PPSA18463 | [BaldursGate3](../plugins/BaldursGate3/README.md) `bg3` | Experimental | Yes: supported v18/LSF primitives, summary tokens, bounded NewAge stack/HP/ability-cache candidates and planned reorder/restore. Gold storage contribution sums are read-only, Inferred; party balance/ownership remain unresolved. Creation-score arrays have a contradictory owner join; no named base-score or respec writer. Candidate storage and source rows are not automatically authoritative party identities. |
| Cyberpunk 2077 gameplay | PPSA04027 | [Cyberpunk2077](../plugins/Cyberpunk2077/README.md) `cyberpunk2077` | Experimental | Yes, for save 265 / game 2210 / archive 195 and applicable RED schema: money, attributes, points, supported stacks, stored progression and existing facts. Explicit unspent attribute/perk/relic point targets now share one preview/apply/undo; existing per-field bounds remain, with no spent-point refund, perk purchase or budget arithmetic. New meanings Inferred; raw facts Unknown. Equipment/perk allocations/garage remain read-only. |
| Elden Ring | PPSA03169 (JP), PPSA04608 (Asia), PPSA04609 (EU), PPSA04610 (US) | [EldenRing](../plugins/EldenRing/README.md) `eldenring` | Experimental | Yes, formats 150/90: held runes, mirrored names/rune history and allowlisted positive ordinary stacks/actions. New writes Inferred; only JP has sample coverage. Attributes, both rune-level mirrors, equipment (17/34 assigned joins) and menu equipment copies are read-only, Inferred; progress remains read-only. Larval Tear ID is catalog/regulation-backed but absent from samples; no Tear grant, stat/equipment writer or proven respec transaction. |
| Final Fantasy XVI retail / demo | PPSA10665 (retail), PPSA13803 (demo) | [FinalFantasy16](../plugins/FinalFantasy16/README.md) `ffxvi` | Experimental | Yes: supported existing IDs/stacks, gear, raw abilities, player/party context and time. Separate Gil/AP/Renown previews require one unchanged unique main-bank resource row and retain the 0–127811 ceiling. Restore-imported previews cover only lower, positive ordinary main-bank stacks: potions/tonics 5, 6, 8–10 and common materials 101–109; no currencies, unique/key/progression/upgrade, ambiguous or secondary rows. Storage Verified; joins/ordinary eligibility Inferred, other meanings Inferred/Unknown. System and unsupported schemas remain read-only. |
| Final Fantasy VII Remake Intergrade / INTERmission | PPSA01911 | [FinalFantasy7](../plugins/FinalFantasy7/README.md) `ff7r` | Experimental | Yes: gil, mapped counts, character values, ordinary materia AP/stars and individual mapped progress bits. New previews recover deficient current HP/MP to stored maxima and batch eligible levels 1–50 with derived XP; maxima/rewards are not recalculated. First-record stored seconds are writable from zero through the immutable opening value only. Writes Inferred (retail time meaning Verified); integrity/progress dependencies unconfirmed. Selection and level-undo defects are linked above. |
| Final Fantasy VII Rebirth retail / demo | PPSA08664 (retail), PPSA18936 (demo) | [FinalFantasy7](../plugins/FinalFantasy7/README.md) `ff7r` | Experimental | Yes: supported values as above, qualified owned-weapon references, current HP/MP recovery, eligible level 1–70 previews with derived XP and first-record stored seconds bounded by opening time. Writes Inferred; retail time meaning Verified, demo Inferred. Selection and level-undo defects are linked above. Maxima/rewards and other time counters are not synchronized. Progress-bit edits can break progression; folio/socket diagrams do not establish safe unlocks. |
| Borderlands 3 | PPSA01463 | [Gearbox](../plugins/Gearbox/README.md) `gearbox` | Experimental | Yes: supported progression, currencies/keys/ammo, serial items/parts, planned inventory operations and explicit character/profile groups. Selected gear can match this character's XP-derived level, clipped to 72; selected complete known records support ordinary/favorite/junk batches preserving unknown bits (both Inferred). Ammo restores only lower supported pools to opening amounts (Verified pool schema), with no SDU/capacity or pool creation. Unsupported/conflicting records skip; byte-only bank/lost-loot records cannot be marked. Console acceptance/item legality remain unproven; Borderlands 4 is excluded. |
| Tiny Tina's Wonderlands | PPSA01493 | [Gearbox](../plugins/Gearbox/README.md) `gearbox` | Experimental | Yes: title-specific progression, currencies/keys/ammo, serial edits and explicit character/profile groups. Selected gear matching uses this character's XP-derived level, clipped to 40; ordinary/favorite/junk batches preserve unknown bits (both Inferred). Ammo restores only lower supported pools to opening amounts (Verified pool schema), without SDU/capacity changes or pool creation. Unsupported/conflicting records skip; profiles cannot supply a character level. Hero/Myth mirror precedence and gameplay budgets remain unproven. |
| Cyberpunk 2077 settings / metadata | PPSA04027 (overlay; no registered JSON title IDs) | [GenericJson](../plugins/GenericJson/README.md) `format.json` | Experimental | Yes: v136 recorded settings/defaults and paired value/index actions; other JSON uses the raw workbench. Meanings Inferred/Unknown. This does not edit binary gameplay. |
| It Takes Two (EU) profile | PPSA02343 (overlay; no registered JSON title IDs) | [GenericJson](../plugins/GenericJson/README.md) `format.json` | Experimental | Yes: present minigame/chapter/checkpoint values and presets, Inferred. No missing gameplay keys or proven story-dependency model. |
| Ridge Racer 2 PSP classic wrapper | PPSA07696 (overlay; no registered JSON title IDs) | [GenericJson](../plugins/GenericJson/README.md) `format.json` | Experimental | Yes: wrapper primitives and imported-value resets, Unknown gameplay interpretation. `DATA.BIN` gameplay is a separate unsupported format. |
| Other valid UTF-8 JSON | Content-based; no registered title IDs | [GenericJson](../plugins/GenericJson/README.md) `format.json` | Experimental | Yes: scalar and structural JSON syntax editing with previews. Unknown game semantics; valid JSON does not prove compatibility. |
| God of War Ragnarök | PPSA08329, PPSA08330, PPSA08331, PPSA08332, PPSA08333, PPSA08334, PPSA25043, PPSA25044 | [GodOfWarRagnarok](../plugins/GodOfWarRagnarok/README.md) `gow-ragnarok` | Experimental | Yes: existing decoded wallet counters bounded by imported values, play time and counter restore. Gear/armor definition and catalog-level joins are read-only, Inferred; these levels describe definition variants, not a decoded upgrade word or legal maximum. Core weapon numeric levels, enchantment slots, named skills and spendable/lifetime XP semantics remain Unknown; no new gear/skill/XP writer. Integrity unknown; preferences read-only. Only PPSA08330 has samples. |
| Ratchet & Clank: Rift Apart | PPSA01474 | [Insomniac](../plugins/Insomniac/README.md) `insomniac` | Experimental | Yes: CareerBolts/SpentBolts, seven existing gold-bolt options, supported boolean preferences and career-counter target preview. Career minus spent is a diagnostic, not the wallet. Inventory bolts and raritanium are read-only, Inferred; absent records are unresolved, not zero. Weapons, armour, upgrades and world views remain read-only candidates; named upgrade nodes lack config/mask/cost/dependency joins. No Spider-Man support claim. |
| Pragmata | PPSA02530 | [Pragmata](../plugins/Pragmata/README.md) `pragmata` | Experimental | Yes: existing IDs/quantities, header/clocks and supported primitives in Gadgets/Loadout/Progress/Quests/World. Restore-imported previews raise only lower nonnegative quantities with unchanged raw IDs; negative/changed-ID rows skip, duplicate IDs or unbound targets reject. Inferred quantity storage and Lunafilament, Upgrade Components and Pure Lunum labels; the other 87 names, including Cabin Coins, remain Unknown. Labels add no write eligibility; existing quantity guards remain, legal caps unproven, no named-resource/mission preset. RSA trailer remains unverified. |
| Ghost of Tsushima: Director's Cut | PPSA02225 | [SuckerPunch](../plugins/SuckerPunch/README.md) `suckerpunch` | Experimental | Yes: existing supported resources/ammo, quantity previews and v48/v49 play time. Restore-imported ammo raises only lower eligible opening counts, updates both copies/checksum and retains version 47–49, flags and 0–32767 gates; no capacity grant. The UI selection defect is linked above. Resources/timer Inferred; ammo has Verified byte structure, unverified console effects. No equipment/technique/world writer. |
| Ghost of Yōtei | PPSA26344 | [SuckerPunch](../plugins/SuckerPunch/README.md) `suckerpunch` | Experimental | Yes: 14 eligible existing quantity types and target-quantity preview, Inferred. Restore-imported quantities remain [deferred](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489232331): eleven samples with zeroed tails cannot establish exact checksum coverage. Independently verified integrity or an accepted nonzero-tail/control capture is needed. Unknown checksum tail coverage; mismatching checksum candidate is inspection-only. Gear, techniques and world state remain read-only. |
| Tales of Arise (EU base game) | PPSA02745 | [TalesOfArise](../plugins/TalesOfArise/README.md) `tales-of-arise` | Experimental | Yes: gald, existing active-character level/HP/SP, recognized consumable quantities and previews, Inferred. Party current/max CP are read-only, Inferred; spend/refill authority and growth/bonus dependencies are unproven. Restore-imported previews raise only lower quantities for existing IDs 1–13 with opening/current quantities both 0–15; no item creation or HP/CP restore. The UI selection/exclusion defect is linked above. Measured 760,856-byte / 30-section gameplay schema only; no Beyond the Dawn claim. System blob is unsupported. |
| Hogwarts Legacy | PPSA01603 | [UnrealGvas](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | Experimental | Yes: bounded existing nonidentity integer/real/text database cells, Inferred, only in unwrapped fully decoded image pairs. Perk points, learned talents and collection/page/map/Field Guide challenge state are read-only, Inferred; generic PerkPoints/PageCollected DataValue writes are disabled. Conflicting tuples/images and unproven effect/reward dependencies block talent/collection grants. Wrapped, partial, malformed and metadata-only cases remain read-only; identities/blobs/reference cells are excluded. |
| STAR WARS Jedi: Fallen Order | PPSA02199 | [UnrealGvas](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | Experimental | Yes: supported existing gameplay GVAS scalars, Inferred. Checksum coverage unproven; `Crc32`, actor-stream lengths and compressed actor data remain read-only. |
| Sackboy: A Big Adventure | PPSA01288 | [UnrealGvas](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | Experimental | Yes: supported existing `General` scalars, Inferred. Checksum coverage unproven; `Checksum` and opaque image payloads remain read-only. |
| Other compatible Unreal GVAS | Content-based | [UnrealGvas](../plugins/UnrealGvas/README.md) `engine.unreal-gvas` | Experimental | Yes where supported: existing primitives and bounded primitive-container remove-last operations. Unknown documents with visible integrity fields remain read-only; generic detection is not title support. |
| The Witcher 3: Wild Hunt – Complete Edition | PPSA04018 | [Witcher3](../plugins/Witcher3/README.md) `witcher3` | Experimental | Yes, supplied SAV3 64 / 23 / 163 with unambiguous player identity: level/XP/points/Crowns, quantities/durability, existing non-core ranks/Gwent counts and repair/refill. Ordinary Gwent count batches are Inferred and retain each existing row's 0–max(3, opening count) bound; leaders/unmapped/missing-pack rows skip. No new card indices, deck edits or acquisition/reward transaction; decreases may invalidate decks. Other recognized schemas retain read-only exploration. |
| Minecraft Dungeons II / supported obfuscated JSON | PPSA16064; transformed JSON also content-based | [XorJson](../plugins/XorJson/README.md) `format.json-xor` | Experimental | Yes: shifted settings, tested offline character tokens/structure and explicit character/global groups. Character gate: SoftVersion 2 / FormatHash 3777917472. Other versions/online or unknown modes are read-only. Gameplay semantics/integrity Unknown; structural/bulk behavior Inferred. |
| Other files / unsupported variants | Any | Built-in raw inspector `builtin.raw` | ReadOnly | No. Inspection does not establish game support. |

## Troubleshooting

### The page will not open or the port is in use

Keep the server terminal open and check its error text. If it reports that the address
is already in use, a toolkit instance may already be running. Stop that instance from
its terminal or follow [Use another port](#use-another-port) and open the new address.
Do not pass a port to `start.cmd`/`start.sh`; they ignore extra arguments. Use `--urls`
on the executable. `ASPNETCORE_URLS` alone does not override the bundled `Urls` setting.
If the server exited, resolve its startup error before retrying the browser.

### Unsupported or corrupt save errors

Check that you imported a complete decrypted dump zip, that it opens in an archive
tool, and that the original title ID/folder layout is intact. A title may be registered
while your build, DLC, file role or region has no proven schema. Check the linked plugin
README and the exact error for length, version, integrity or decoding limits. An
unsupported variant can appear in the raw inspector instead of a game editor.

For a damaged or incomplete download/dump, retry from your untouched source. Do not
rename another game's files or alter checksum/version bytes to force recognition. If an
edit is rejected, correct its value or undo it. If a table/preview says it is out of date,
refresh the document and preview again with the current selection. Clear a column filter to
restore all rows; a blank sort selection uses the collection default.
Report export failures with their error text and the smallest sequence of edits that triggers them.

### macOS Gatekeeper or quarantine blocks launch

Bundles are unsigned and not notarized. macOS may quarantine downloads and block the
executable or a bundled native library. Verify the release checksum and correct Mac
architecture first. After attempting to start, use **System Settings → Privacy & Security
→ Open Anyway** for the blocked trusted component if macOS offers it, then retry
`./start.sh`. Follow [Apple's instructions for opening trusted unsigned software](https://support.apple.com/en-gb/102445).
If a specific component remains blocked, record its name and the exact message for an
issue report. Avoid disabling Gatekeeper globally. An execute-permission error is
handled separately below.

### Linux or macOS says permission denied

From the extracted bundle folder, restore execute permission to both launch files:

```sh
chmod +x start.sh SaveToolkit.Api
./start.sh
```

If permission is still denied, extract to a writable location on a filesystem that
allows execution. A `noexec` mount cannot be fixed with `chmod`. A missing ICU/OpenSSL
or other native-library error requires a supported OS with those runtime dependencies;
installing Node.js does not address it.

### Edits disappeared or the workspace is gone

Closing a dump, server restart or idle expiration ends the workspace. Re-import your
original or last exported zip. There is no persistent project/autosave recovery from
`data/workspaces/`; export before ending a session.

## Report a problem

Search existing [GitHub issues](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues),
then open an issue there (repository access is required while private). Include the
version from the home-page footer, OS/architecture, plugin, title ID, game build/DLC if
known, affected filename, exact error and steps to reproduce. For editing problems,
record the field/action, original and requested gameplay values, preview scope, and
whether undo, export/re-import or a console reload produced the problem.

Redact account identifiers and personal paths from error text or screenshots. Do not
post decrypted saves, passwords, account data or private dumps in an issue. Describe
the problem first and arrange any necessary sample sharing privately with a maintainer.
Console acceptance and controlled gameplay evidence are tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
