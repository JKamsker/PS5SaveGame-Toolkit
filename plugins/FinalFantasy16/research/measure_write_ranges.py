"""Measure aggregate experimental-write bounds without exporting save bytes.

Schema: Nenkai/010GameTemplates, MIT, revision 4a3685f9b63f72ca49e24ac2f18420aaad2762b8:
https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt
Uncontrolled samples establish observed ranges, not legal gameplay limits.
Run with python -I and SAVETOOLKIT_ARTIFACTS; stdout contains aggregate metadata only.
"""
import collections
import json
import os
from pathlib import Path
import struct
import zipfile
import zlib

TITLES = ("PPSA10665", "PPSA13803")
HEADER_SIZE = 16
BLOCK_SIZE_ADJUSTMENT = 8
BLOCK_PAYLOAD_ADJUSTMENT = 24
ITEM_BANKS = (("main", 0, 1200), ("secondary", 33624, 100))
ITEM_RECORD_SIZE = 16
SUPPORTED = {
    1: (202211161, (272, 1536)),
    2: (2021112401, (168,)),
    6: (2021110401, (36448,)),
    7: (2021061001, (104,)),
    13: (20220104, (72,)),
    27: (20210422, (96,)),
}
PLAYER_FIELDS = ("level", "xp", "childLevel", "childXp", "hp", "recovery", "lb",
                 "currentSummon", "learnedSummons", "revealedSummons")


def read_block(values, kind, data, offset):
    if kind == 1:
        for index, name in enumerate(PLAYER_FIELDS):
            values[name].append(struct.unpack_from("<i", data, offset + index * 4)[0])
        for name, start, count in (("summons", 40, 3), ("commands", 52, 22), ("shortcuts", 140, 4)):
            values[name].extend(struct.unpack_from("<" + "i" * count, data, offset + start))
    elif kind == 2:
        for name, start in (("scene", 0), ("map", 4), ("party", 16), ("memberCount", 20)):
            values[name].append(struct.unpack_from("<i", data, offset + start)[0])
    elif kind == 7:
        values["playTime"].append(struct.unpack_from("<i", data, offset)[0])
    elif kind == 13:
        values["equipment"].extend(struct.unpack_from("<18i", data, offset))
    elif kind == 27:
        values["ranks"].extend(data[offset:offset + 68])
        values["skillFlags"].extend(data[offset + 68:offset + 92])
    elif kind == 6:
        for bank, start, capacity in ITEM_BANKS:
            count = struct.unpack_from("<Q", data, offset + start + capacity * 20)[0]
            assert count <= capacity
            for index in range(count):
                item, quantity = struct.unpack_from("<ii", data, offset + start + index * ITEM_RECORD_SIZE)
                values[bank + "Quantity"].append(quantity)
                values[bank + "Id"].append(item)


def measure(title):
    values = collections.defaultdict(list)
    archive_path = Path(os.environ["SAVETOOLKIT_ARTIFACTS"]) / f"{title}_saves_dec.zip"
    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            if not entry.filename.rsplit("/", 1)[-1].startswith("game-save-data"):
                continue
            data = archive.read(entry)
            assert struct.unpack_from("<I", data)[0] == HEADER_SIZE
            assert struct.unpack_from("<I", data, 4)[0] == zlib.crc32(data[HEADER_SIZE:])
            position = HEADER_SIZE
            seen = set()
            while (kind := struct.unpack_from("<I", data, position)[0]) != 0:
                _, size, version = struct.unpack_from("<IIQ", data, position)
                assert size >= BLOCK_PAYLOAD_ADJUSTMENT and size % 8 == 0
                assert position + size - BLOCK_SIZE_ADJUSTMENT <= len(data)
                if kind in SUPPORTED:
                    expected_version, lengths = SUPPORTED[kind]
                    assert kind not in seen and version == expected_version
                    assert size - BLOCK_PAYLOAD_ADJUSTMENT in lengths
                    assert not (title == "PPSA13803" and kind == 1 and size - BLOCK_PAYLOAD_ADJUSTMENT != 272)
                    seen.add(kind)
                    read_block(values, kind, data, position + HEADER_SIZE)
                position += size - BLOCK_SIZE_ADJUSTMENT
            assert position == len(data) - 4
    return {key: {"min": min(items), "max": max(items)} for key, items in values.items()}


if __name__ == "__main__":
    print(json.dumps({title: measure(title) for title in TITLES}, indent=2))
