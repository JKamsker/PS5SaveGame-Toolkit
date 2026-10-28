PRAGMATA PS5 SAVE RESEARCH TOOLKIT (PPSA02530)
===========================================

Start with FORMAT.txt for the binary layout and verified_save_map.json for
exact per-file field paths, values, plaintext offsets and ciphertext offsets.

This is a small command-line foundation for an editor, not a graphical app.
It supports the 12 supplied files: DSSS version 2, flags 0x10, PS5 key 526253637.

WHAT IS VERIFIED

* All 12 files fully decrypt and parse, including slots 2, 4, 6, 8 and 10
  that the earlier parser could not completely read.
* All 503 Mandarin blocks pass CityHash64 and embedded key/IV checks.
* Every file passes its outer MurmurHash3 check.
* No-op reconstruction is byte-identical for all files.
* A quantity edit in each of the 11 gameplay files survives decryption,
  integrity validation and reparsing. Reversing it restores the original bytes.
* The original two summary JSON files are identical. Their core numerical
  inventory, time and header values were reproduced, but several labels remain
  hypotheses. See FORMAT.txt and the semantic_caveats in the map.

WHAT IS STILL UNKNOWN

* Human-readable item names and many gameplay field names.
* The names represented by difficulty, inheritance-mode and other hashed IDs.
* The in-game effect and valid range of arbitrary changes.
* Whether a modified save loads on your console: no PS5 load test was performed.

Keep a backup. The tool writes a NEW output file and refuses to overwrite
existing files. It preserves unknown data and only supports fixed-size edits.

INSTALL

Python 3.10 or newer:

    python -m pip install -r requirements.txt

Examples assume the toolkit files and an individual save .bin are in the
current directory. Paths can also be absolute or point into the extracted ZIP.

INSPECT / EXPORT

    python pragmata_save.py inspect data000.bin
    python pragmata_save.py dump data000.bin data000.tree.json
    python pragmata_save.py decrypt data000.bin data000.plaintext.bin
    python pragmata_save.py roundtrip data000.bin data000.copy.bin

dump exports the full typed tree plus scalar paths and offsets. The full dump
is considerably larger than the original binary. decrypt exports only the
serialized plaintext stream, without a DSSS header or checksums; it is a
research artifact and cannot be placed back onto the console directly.

EDIT AN EXISTING ITEM

    python pragmata_save.py set-item data000.bin data000.edited.bin 2227368435 9999

This changes the quantity for existing item ID 2227368435. Its in-game name
is NOT known. It does not change the item ID, add entries, change array counts,
or resize the save. Values accepted by the binary are not necessarily legal
in the game.

EDIT A FIXED-SIZE SCALAR BY ITS EXACT PATH

    python pragmata_save.py set-field data000.bin data000.edited.bin "7c416083/7c018857[6]/3185b7a5[5]/d18b252d" 9999

Use a path from a fresh dump of the specific file being edited. Section order
and absolute offsets vary between slots. set-item locates inventory by its
class hash and avoids relying on the section index.

String/struct edits and resized arrays are intentionally unsupported. Existing
primitive bool/int/float fields can be changed with set-field. All edits repair
the changed block checksum(s) and outer checksum, then validate the output
before it is written.

RUN THE INTEGRITY / MUTATION TESTS AGAINST YOUR OWN EXTRACTED FILES

    python selftest.py path/to/PPSA02530

The tests decrypt, parse, perform and reverse a quantity edit, and check that
encrypted-data corruption is rejected even after repairing the outer hash.
They do not write modified save files.

EXISTING GRAPHICAL EDITOR

https://github.com/kvasszn/ree-save-editor

The project's README documents PRAGMATA PS5 support, key/ID 526253637 and
_RegionHash 3208943443. This is a useful alternative to building a GUI from
scratch. Those values are independently verified against these files.

PACKAGING / RESTORING

The supplied ZIP has already had the PS5 container layer removed. This tool
only handles the game's remaining DSSS/Mandarin layer. Restore a modified
data*.bin through the same save-manager/container workflow used to obtain
the original decrypted files. The tool does not create or sign PS5 save
containers and does not modify sce_sys/param.sfo or keystone.
