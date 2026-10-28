"""Exhaustive candidate-integrity search for the decrypted FFVII linked records.

This is read-only research tooling. It scans record bodies, record headers plus bodies,
and whole-file ranges against every metadata word in envelopes/content headers/tails.
Run it from the repository worktree with the artifacts directory as its argument.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import struct
import zipfile
import zlib


def crc(data: bytes, width: int, polynomial: int, reflected: bool, init: int, xorout: int) -> int:
    mask = (1 << width) - 1
    value = init & mask
    top = 1 << (width - 1)
    for byte in data:
        if reflected:
            value ^= byte
            for _ in range(8):
                value = (value >> 1) ^ polynomial if value & 1 else value >> 1
        else:
            value ^= byte << (width - 8)
            for _ in range(8):
                value = ((value << 1) ^ polynomial) & mask if value & top else (value << 1) & mask
    return (value ^ xorout) & mask


def fnv(data: bytes, bits: int) -> int:
    value = 0x811C9DC5 if bits == 32 else 0xCBF29CE484222325
    prime = 0x01000193 if bits == 32 else 0x100000001B3
    mask = (1 << bits) - 1
    for byte in data:
        value = ((value ^ byte) * prime) & mask
    return value


def xxhash(data: bytes, bits: int) -> int:
    # Reference xxHash, seed zero; kept here instead of adding a package dependency.
    if bits == 32:
        p1, p2, p3, p4, p5 = 0x9E3779B1, 0x85EBCA77, 0xC2B2AE3D, 0x27D4EB2F, 0x165667B1
        word, mask = 4, 0xFFFFFFFF
        def avalanche(value: int) -> int:
            value ^= value >> 15; value = value * p2 & mask
            value ^= value >> 13; value = value * p3 & mask
            return value ^ (value >> 16)
    else:
        p1, p2, p3, p4, p5 = (0x9E3779B185EBCA87, 0xC2B2AE3D27D4EB4F,
                               0x165667B19E3779F9, 0x85EBCA77C2B2AE63, 0x27D4EB2F165667C5)
        word, mask = 8, 0xFFFFFFFFFFFFFFFF
        def avalanche(value: int) -> int:
            value ^= value >> 33; value = value * p2 & mask
            value ^= value >> 29; value = value * p3 & mask
            return value ^ (value >> 32)
    def rot(value: int, count: int) -> int:
        return ((value << count) | (value >> (bits - count))) & mask
    def read(pos: int) -> int:
        return int.from_bytes(data[pos:pos + word], "little")
    def round_(acc: int, value: int) -> int:
        return rot((acc + value * p2) & mask, 31 if bits == 32 else 31) * p1 & mask
    length = len(data)
    pos = 0
    if length >= word * 4:
        acc = [p1 + p2, p2, 0, -p1]
        while pos + word * 4 <= length:
            for index in range(4):
                acc[index] = round_(acc[index], read(pos))
                pos += word
        value = (rot(acc[0], 1) + rot(acc[1], 7) + rot(acc[2], 12) + rot(acc[3], 18)) & mask
        for item in acc:
            value = (value ^ round_(0, item)) * p1 + p4 & mask
    else:
        value = p5
    value = (value + length) & mask
    if bits == 64:
        while pos + 8 <= length:
            value = (value ^ (rot((read(pos) * p2) & mask, 31) * p1 & mask)) * p1 + p4 & mask
            pos += 8
        if pos + 4 <= length:
            value = (value ^ (int.from_bytes(data[pos:pos + 4], "little") * p3 & mask)) * p1 + p4 & mask
            pos += 4
    else:
        while pos + 4 <= length:
            value = (value ^ (int.from_bytes(data[pos:pos + 4], "little") * p3 & mask)) * p4 & mask
            value = rot(value, 17) * p1 & mask
            pos += 4
    while pos < length:
        value = (value ^ (data[pos] * p5 & mask)) * p1 & mask
        value = rot(value, 11) * p2 & mask
        pos += 1
    return avalanche(value)


def murmur3(data: bytes) -> int:
    value = 0
    for start in range(0, len(data) - 3, 4):
        k = int.from_bytes(data[start:start + 4], "little") * 0xCC9E2D51 & 0xFFFFFFFF
        k = ((k << 15) | (k >> 17)) & 0xFFFFFFFF
        k = k * 0x1B873593 & 0xFFFFFFFF
        value ^= k
        value = (((value << 13) | (value >> 19)) * 5 + 0xE6546B64) & 0xFFFFFFFF
    tail = data[len(data) - len(data) % 4:]
    k = sum(byte << (8 * i) for i, byte in enumerate(tail))
    if tail:
        k = k * 0xCC9E2D51 & 0xFFFFFFFF
        value ^= ((k << 15) | (k >> 17)) & 0xFFFFFFFF
        value = value * 0x1B873593 & 0xFFFFFFFF
    value ^= len(data)
    value ^= value >> 16
    value = value * 0x85EBCA6B & 0xFFFFFFFF
    value ^= value >> 13
    value = value * 0xC2B2AE35 & 0xFFFFFFFF
    return value ^ (value >> 16)


def hashes(data: bytes) -> dict[str, int]:
    result = {"adler32": zlib.adler32(data), "fnv1a32": fnv(data, 32), "fnv1a64": fnv(data, 64),
              "xxhash32": xxhash(data, 32), "xxhash64": xxhash(data, 64), "murmur3": murmur3(data),
              "md5_32": int.from_bytes(hashlib.md5(data).digest()[:4], "little"),
              "sha1_32": int.from_bytes(hashlib.sha1(data).digest()[:4], "little"),
              "sha1_64": int.from_bytes(hashlib.sha1(data).digest()[:8], "little")}
    for name, width, poly in (("crc32", 32, 0x04C11DB7), ("crc32c", 32, 0x1EDC6F41), ("crc64", 64, 0x42F0E1EBA9EA3693)):
        reflected_poly = {32: {0x04C11DB7: 0xEDB88320, 0x1EDC6F41: 0x82F63B78}, 64: {poly: 0xC96C5795D7870F42}}[width][poly]
        for reflected in (False, True):
            selected = reflected_poly if reflected else poly
            for init in (0, (1 << width) - 1):
                for xorout in (0, (1 << width) - 1):
                    suffix = f"_{'r' if reflected else 'n'}_{init:x}_{xorout:x}"
                    result[name + suffix] = crc(data, width, selected, reflected, init, xorout)
    result.update({"sum8": sum(data) & 0xFF, "sum16": sum(int.from_bytes(data[i:i + 2], "little") for i in range(0, len(data) - 1, 2)) & 0xFFFF,
                   "sum32": sum(int.from_bytes(data[i:i + 4], "little") for i in range(0, len(data) - 3, 4)) & 0xFFFFFFFF,
                   "sum64": sum(int.from_bytes(data[i:i + 8], "little") for i in range(0, len(data) - 7, 8)) & 0xFFFFFFFFFFFFFFFF})
    return result


def records(data: bytes) -> list[tuple[int, int, bool]]:
    result = []
    offset = 0
    while True:
        next_offset = struct.unpack_from("<I", data, offset + 4)[0]
        tail = data[offset] & 0xFF == 4
        result.append((offset, len(data) if tail else next_offset, tail))
        if tail:
            return result
        offset = next_offset


def targets(data: bytes, spans: list[tuple[int, int, bool]]) -> list[tuple[int, int, str]]:
    result = []
    for index, (start, end, tail) in enumerate(spans):
        for width in (4, 8):
            for offset in range(start, min(end, start + 40), 4):
                if offset + width <= end:
                    result.append((offset, width, f"record{index}/header"))
            if tail:
                for offset in range(start, end, 4):
                    if offset + width <= end:
                        result.append((offset, width, f"tail"))
    return list(dict.fromkeys(result))


def scan(data: bytes) -> dict[str, int]:
    spans = records(data)
    ranges = [(start + 40, end, f"record{index}/body") for index, (start, end, tail) in enumerate(spans) if not tail]
    ranges += [(start, end, f"record{index}/full") for index, (start, end, tail) in enumerate(spans) if not tail]
    ranges += [(0, spans[-1][0], "file/without-tail"), (0, len(data), "file/full")]
    found = 0
    tested = 0
    omitted_found = 0
    omitted_tested = 0
    metadata = targets(data, spans)
    for start, end, _ in ranges:
        values = hashes(data[start:end])
        for offset, width, _ in metadata:
            if start <= offset and offset + width <= end:
                tested += len(values)
                stored = int.from_bytes(data[offset:offset + width], "little")
                found += sum(value == stored for value in values.values())
                if start == 0 and end in (spans[-1][0], len(data)):
                    omitted = data[start:offset] + data[offset + width:end]
                    omitted_values = hashes(omitted)
                    omitted_tested += len(omitted_values)
                    omitted_found += sum(value == stored for value in omitted_values.values())
    return {"records": len(spans), "ranges": len(ranges), "metadata_words": len(metadata),
            "candidates_tested": tested, "matches": found,
            "whole_file_minus_field_tested": omitted_tested, "whole_file_minus_field_matches": omitted_found}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("artifacts", type=pathlib.Path)
    args = parser.parse_args()
    report = {}
    for archive in sorted(args.artifacts.glob("PPSA*_saves_dec*.zip")):
        entries = []
        with zipfile.ZipFile(archive) as source:
            for entry in source.infolist():
                name = pathlib.PurePosixPath(entry.filename).name
                if entry.is_dir() or not name.startswith(("ff7remake", "ff7rebirth")):
                    continue
                data = source.read(entry)
                entries.append({"file": entry.filename, "bytes": len(data), **scan(data)})
        report[archive.stem] = entries
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
