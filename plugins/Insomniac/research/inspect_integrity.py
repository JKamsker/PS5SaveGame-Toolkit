"""Read the original zip directly, preserving duplicate paths; never extract samples.

Usage: python inspect_integrity.py <PPSA01474_saves_dec.zip>
No output is written to disk. A lack of hits cannot establish checksum-free writes.
"""

import argparse
import collections
import functools
import struct
import sys
import zipfile

sys.dont_write_bytecode = True
from hash_algorithms import hashes, verify_vectors


@functools.lru_cache(maxsize=256)
def cached_hashes(data):
    return hashes(data)


def sections(data):
    count = struct.unpack_from("<H", data, 12)[0]
    for index in range(count):
        yield struct.unpack_from("<III", data, 16 + index * 12)


def outside_hits(data, pattern, start, end):
    position = data.find(pattern)
    while position >= 0:
        if position + len(pattern) <= start or position >= end:
            yield position
        position = data.find(pattern, position + 1)


def inspect(archive):
    verify_vectors()
    hits = collections.Counter()
    tag_counts = collections.Counter()
    degenerate = tests = records = 0
    with zipfile.ZipFile(archive) as source:
        for ordinal, entry in enumerate(source.infolist()):
            if not entry.filename.endswith("/i29-s.save") or "sdimg_sce_bu_" in entry.filename:
                continue
            data = source.read(entry)
            records += 1
            print(f"Record {records}: zip entry {ordinal} {entry.filename}", flush=True)
            for tag, offset, size in sections(data):
                tag_counts[tag] += 1
                coverages = {"full": (0, size), "skip4": (4, size), "skip8": (8, size),
                             "trim4": (0, size - 4), "trim8": (0, size - 8)}
                for coverage, (start, end) in coverages.items():
                    if start >= end:
                        continue
                    begin, finish = offset + start, offset + end
                    for algorithm, (value, width) in cached_hashes(data[begin:finish]).items():
                        tests += 1
                        if value in (0, (1 << (width * 8)) - 1):
                            degenerate += 1
                            continue
                        for endian in ("little", "big"):
                            for match in outside_hits(data, value.to_bytes(width, endian), begin, finish):
                                hits[(f"{tag:08X}", coverage, algorithm, endian, match - offset)] += 1
    print(f"Records={records}; digest candidates={tests}; degenerate candidates ignored={degenerate}")
    print("Section counts:", {f"{tag:08X}": count for tag, count in tag_counts.items()})
    print("Nondegenerate matches outside the hashed byte range (candidates, not validated integrity):")
    for candidate, count in sorted(hits.items()):
        print(count, candidate)
    print(f"Candidate match locations={len(hits)}; occurrences={sum(hits.values())}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive")
    inspect(parser.parse_args().archive)
