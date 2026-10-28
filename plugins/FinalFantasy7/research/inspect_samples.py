"""Read-only FFVII archive measurements. Never extracts or executes sample/reference data."""

import argparse
import collections
import json
import pathlib
import re
import struct
import zipfile
import zlib

TITLES = ("PPSA01911", "PPSA08664", "PPSA18936")
GAME_FILE = re.compile(r"ff7(?:remake(?:plus)?|rebirth)(?:\d{3}|(?:demo)?common)(?:\.sav)?$")


def walk(data):
    records = []
    offset = 0
    while len(records) < 512:
        if not 0 <= offset <= len(data) - 16:
            raise ValueError("Record header outside file")
        marker, next_offset, size, flags = struct.unpack_from("<4I", data, offset)
        if marker & 255 == 4:
            if offset != len(data) - 16:
                raise ValueError("Tail does not end at EOF")
            return records, (marker, next_offset, size, flags)
        if not offset + 40 <= next_offset <= len(data) - 16:
            raise ValueError("Cyclic, overlapping or out-of-bounds link")
        repeated_tag = data[offset + 16:offset + 32]
        version, declared = struct.unpack_from("<2I", data, offset + 32)
        if repeated_tag != bytes(16) and repeated_tag != repeated_tag[:4] * 4:
            raise ValueError("Non-repeated tag")
        tag = repeated_tag[:4].decode("ascii") if any(repeated_tag) else "Empty"
        records.append((offset, next_offset, size, flags, tag, version, declared))
        offset = next_offset
    raise ValueError("Record limit exceeded")


def measure(archive, title):
    lengths = collections.Counter()
    chains = collections.Counter()
    tags = collections.Counter()
    tails = collections.Counter()
    crc_tests = 0
    crc_matches = 0
    gvas_files = 0
    gameplay = []
    with zipfile.ZipFile(archive) as source:
        # infolist + read(ZipInfo) retains duplicate paths and backup twins.
        for entry in source.infolist():
            name = pathlib.PurePosixPath(entry.filename).name
            if entry.is_dir() or not GAME_FILE.fullmatch(name):
                continue
            data = source.read(entry)
            records, tail = walk(data)
            lengths[len(data)] += 1
            chains[len(records) + 1] += 1
            tails[str(tail)] += 1
            gvas_files += int(b"GVAS" in data)
            for offset, next_offset, size, flags, tag, _, declared in records:
                tags[tag] += 1
                if declared:
                    # Limited candidate test only: two header words, two coverage ranges.
                    for start in (offset + 16, offset + 40):
                        crc = zlib.crc32(data[start:next_offset])
                        crc_tests += 2
                        crc_matches += int(crc == size) + int(crc == flags)
            if "common" not in name and "sce_bu_" not in entry.filename:
                remake = title == "PPSA01911"
                base, stride, gil_id, playtime = (0x34DD8, 24, 20, 0x2B0) if remake else (0x4FA70, 32, 1, 0x3C30)
                gil = []
                for slot in range(2048):
                    item, quantity = struct.unpack_from("<Ii", data, base + slot * stride)
                    if item == gil_id:
                        gil.append({"slot": slot, "quantity": quantity})
                gameplay.append({"file": name, "play_seconds": struct.unpack_from("<I", data, playtime)[0], "gil": gil})
    return {"lengths": lengths, "chain_counts": chains, "tags": tags, "tails": tails,
            "crc_candidates_tested": crc_tests, "crc_matches": crc_matches, "files_with_GVAS_bytes": gvas_files,
            "primary_gameplay": gameplay}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=pathlib.Path)
    args = parser.parse_args()
    report = {}
    for title in TITLES:
        archives = sorted(args.artifacts.glob(title + "_saves_dec*.zip"), key=lambda p: (len(str(p)), str(p)))
        report[title] = measure(archives[0], title) if archives else {"missing": True}
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
