"""Invented in-memory records, never extracted from a private save."""
from pathlib import Path
import struct
import warnings
import zipfile
import zlib

TITLES = ("PPSA10665", "PPSA13803")
HEADER_SIZE = 16
ITEM_PAYLOAD_SIZE = 36448
ALLOCATION_OFFSET = 1200 * 20
ITEM_VERSION = 2021110401


def repair_crc(data):
    struct.pack_into("<I", data, 4, zlib.crc32(data[HEADER_SIZE:]))
    return bytes(data)


def container(*, inventory=False, populated=True):
    payload = bytearray(ITEM_PAYLOAD_SIZE if inventory else 8)
    if inventory and populated:
        struct.pack_into("<iiii", payload, 0, 1, 7, 0, 0)
        struct.pack_into("<iiii", payload, 16, 999999, 2, 0, 0)
        struct.pack_into("<Q", payload, ALLOCATION_OFFSET, 2)
    header = struct.pack("<IIII", HEADER_SIZE, 0, 0, 0)
    block = struct.pack("<IIQ", 6 if inventory else 1, len(payload) + 24,
                        ITEM_VERSION if inventory else 1)
    return repair_crc(bytearray(header + block + payload + struct.pack("<I", 0)))


def write_archives(root, first_record=None):
    for title in TITLES:
        with zipfile.ZipFile(Path(root) / f"{title}_saves_dec.zip", "w") as archive:
            entries = [("ignored/readme.txt", b"ignored"),
                       ("slot/game-save-data", first_record if first_record is not None
                        else container(inventory=True)),
                       ("slot/game-save-data", container(inventory=True)),
                       ("empty/game-save-data-01", container(inventory=True, populated=False)),
                       ("system-save-data", container())]
            for name, data in entries:
                info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = 0o600 << 16
                with warnings.catch_warnings():
                    warnings.filterwarnings("ignore", message="Duplicate name:", category=UserWarning)
                    archive.writestr(info, data)
