"""Read-only, reproducible archive measurements. Never extracts or prints save strings.

Run: python docs/formats/inspect_samples.py ARTIFACTS OUTPUT_JSON
OUTPUT_JSON must be outside the repository. Duplicate ZIP entries remain separate.
YouTube application payloads are deliberately excluded (may contain account data).
"""
import collections
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import zipfile
import zlib
from inspect_containers import inspect as inspect_containers

TITLES = "01288 01325 01463 01474 01493 01494 01506 01651 01876 01911 01982 02745 03169 04018 04027 06251 07696 07821 08330 08664 10665 13803 18936".split()
SIGNATURES = {"GVAS": b"GVAS", "DSSS": b"DSSS", "LZ4": bytes.fromhex("04224d18"),
              "zstd": bytes.fromhex("28b52ffd"), "gzip": b"\x1f\x8b\x08", "xz": b"\xfd7zXZ\x00",
              "SQLite": b"SQLite format 3\x00", "LZ4legacy": bytes.fromhex("02214c18"),
              "XML declaration": b"<?xml", "XML Common": b"<Common>", "CLZF": b"FZLC", "LZ4 block wrapper": b"4ZLX",
              "SAV3": b"SAV3", "Tales save": bytes.fromhex("30160510")}


def entropy(data):
    counts = collections.Counter(data)
    return round(-sum((n / len(data)) * math.log2(n / len(data)) for n in counts.values()), 4) if data else 0


def measure(data):
    sample = data[:1048576]
    result = {"size": len(data), "sha256": hashlib.sha256(data).hexdigest(),
              "head": data[:32].hex(" "), "tail": data[-16:].hex(" "),
              "entropy_first_mib": entropy(sample), "zero_fraction_first_mib": round(sample.count(0) / max(1, len(sample)), 4),
              "u32le": list(struct.unpack("<" + "I" * (min(len(data), 32) // 4), data[:min(len(data), 32) // 4 * 4])),
              "signatures": {}}
    for name, sig in SIGNATURES.items():
        offset = data.find(sig)
        if offset >= 0:
            result["signatures"][name] = offset
    if len(data) >= 16:
        result["crc32_after_0x10_matches_u32_at_4"] = zlib.crc32(data[16:]) == struct.unpack_from("<I", data, 4)[0]
    if len(data) > 0x32078 and data[0x32058:0x3205c] == bytes.fromhex("30160510"):
        result["tales_header_0x32058"] = data[0x32058:0x32078].hex(" ")
    try:
        result.update(inspect_containers(data))
    except (ValueError, IndexError, struct.error) as error:
        result["container_check_error"] = type(error).__name__
    try:
        # No application values enter the report, including JSON values.
        parsed = json.loads(data.decode("utf-8-sig"))
        result["json"] = {"root": type(parsed).__name__, "entries": len(parsed) if isinstance(parsed, (dict, list)) else 1}
    except (ValueError, UnicodeError):
        pass
    result["zlib"] = []
    for offset in range(min(len(data) - 1, 4096)):
        a, b = data[offset:offset + 2]
        if a == 0x78 and (a * 256 + b) % 31 == 0:
            try:
                decoder = zlib.decompressobj()
                raw = decoder.decompress(data[offset:], 64 * 1024 * 1024)
                if decoder.eof:
                    result["zlib"].append({"offset": offset, "decoded_size": len(raw),
                                           "consumed": len(data) - offset - len(decoder.unused_data),
                                           "decoded_head": raw[:16].hex(" "), "decoded_entropy": entropy(raw[:1048576])})
            except zlib.error:
                pass
    return result


def inspect(archive, title):
    groups = collections.defaultdict(list)
    with zipfile.ZipFile(archive) as zip_file:
        for entry in zip_file.infolist():
            if entry.is_dir() or "/sce_sys/" in entry.filename:
                continue
            if title == "PPSA01651":
                groups[entry.filename].append({"size": entry.file_size, "excluded": "application account payload"})
                continue
            data = zip_file.read(entry)
            measurements = measure(data)
            previous = groups[entry.filename]
            if previous:
                old = zip_file.read(previous[-1]["zip_index"])
                different = [i for i, (a, b) in enumerate(zip(old, data)) if a != b]
                measurements["previous_diff"] = {"changed_common_bytes": len(different), "size_delta": len(data) - len(old),
                                                  "first": different[0] if different else None, "last": different[-1] if different else None}
            measurements["zip_index"] = entry
            groups[entry.filename].append(measurements)
        for path, generations in groups.items():
            twin_path = path.replace("/sdimg_", "/sdimg_sce_bu_", 1)
            if "/sdimg_sce_bu_" not in path and twin_path in groups:
                for i, record in enumerate(generations):
                    if i >= len(groups[twin_path]):
                        continue
                    twin = groups[twin_path][i]
                    if "sha256" in record:
                        record["backup_identical"] = record["sha256"] == twin.get("sha256")
            for record in generations:
                record.pop("zip_index", None)
    return dict(groups)


def main():
    artifacts, output = map(Path, sys.argv[1:])
    repository = Path(__file__).resolve().parents[2]
    if output.resolve().is_relative_to(repository):
        raise ValueError("Write the measurements outside the repository.")
    report = {}
    for suffix in TITLES:
        title = "PPSA" + suffix
        archive = next(artifacts.glob(title + "_saves_dec*.zip"))
        report[title] = inspect(archive, title)
        print(title, len(report[title]), "paths", flush=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
