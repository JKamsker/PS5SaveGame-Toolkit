"""Read-only corpus evidence. Emits metadata/hashes, never save bytes or personal data.

python -I plugins/FinalFantasy16/research/summarize_corpus.py > <scratch>/corpus.json
SAVETOOLKIT_ARTIFACTS must point to the owner's archives. No third-party code runs.
"""
import collections
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import zipfile
import zlib

HEADER = 16
MAIN_CAPACITY = 1200
RECORD_SIZE = 16
ALLOCATION_METADATA = MAIN_CAPACITY * 20
ARTIFACTS = Path(os.environ["SAVETOOLKIT_ARTIFACTS"])
PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.wiki.en/items.json"
NAMES = {int(row["id"]) for row in json.loads(PACK.read_text(encoding="utf-8"))["records"]}


def summarize(title):
    archive_path = ARTIFACTS / f"{title}_saves_dec.zip"
    counts = collections.Counter()
    blocks = collections.Counter()
    contents = set()
    ids = set()
    records = []
    occurrences = collections.Counter()
    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            name = entry.filename.rsplit("/", 1)[-1]
            if not (name.startswith("game-save-data") or name == "system-save-data"):
                continue
            data = archive.read(entry)
            digest = hashlib.sha256(data).hexdigest()
            contents.add(digest)
            assert struct.unpack_from("<I", data)[0] == HEADER
            assert struct.unpack_from("<I", data, 4)[0] == zlib.crc32(data[HEADER:])
            counts["records"] += 1
            counts["crcValid"] += 1
            role = "system" if name == "system-save-data" else "gameplay"
            counts[role] += 1
            position = HEADER
            item_offset = None
            while (kind := struct.unpack_from("<I", data, position)[0]):
                _, size, version = struct.unpack_from("<IIQ", data, position)
                assert size >= 24 and size % 8 == 0 and position + size - 8 <= len(data)
                blocks[(kind, version, size - 24)] += 1
                if kind == 6:
                    assert version == 2021110401 and size - 24 == 36448
                    item_offset = position + HEADER
                position += size - 8
            assert position == len(data) - 4
            counts["framingValid"] += 1
            if item_offset is not None:
                size = struct.unpack_from("<Q", data, item_offset + ALLOCATION_METADATA)[0]
                assert size <= MAIN_CAPACITY
                ids.update(struct.unpack_from("<i", data, item_offset + index * RECORD_SIZE)[0] for index in range(size))
                if size:
                    counts["populatedGameplay"] += 1
                    # A regression test of the existing writer, not a console acceptance claim.
                    assert struct.unpack_from("<i", data, item_offset)[0] == 1
                    patched = bytearray(data)
                    old = struct.unpack_from("<i", data, item_offset + 4)[0]
                    struct.pack_into("<i", patched, item_offset + 4, old + 1)
                    struct.pack_into("<I", patched, 4, zlib.crc32(patched[HEADER:]))
                    assert all(a == b for i, (a, b) in enumerate(zip(data, patched))
                               if not (4 <= i < 8 or item_offset + 4 <= i < item_offset + 8))
                    counts["fixedWidthPatchChecks"] += 1
            records.append({"path": entry.filename, "occurrence": occurrences[entry.filename],
                            "role": role, "length": len(data), "sha256": digest})
            occurrences[entry.filename] += 1
    return {"titleId": title, "archive": archive_path.name,
            "archiveSha256": hashlib.sha256(archive_path.read_bytes()).hexdigest(),
            "counts": dict(sorted(counts.items())), "distinctContents": len(contents),
            "distinctMainIds": len(ids), "namedMainIds": len(ids & NAMES),
            "explicitUnknownMainIds": len(ids - NAMES),
            "blockTriples": [{"type": t, "version": v, "payloadLength": n, "occurrences": count}
                             for (t, v, n), count in sorted(blocks.items())], "records": records}


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--summary", action="store_true", help="Omit individual file identities; retain aggregate metadata only")
args = parser.parse_args()
titles = [summarize(title) for title in ("PPSA10665", "PPSA13803")]
if args.summary:
    for title in titles:
        del title["records"]
print(json.dumps({"format": "ffxvi-corpus-evidence/1", "analyzedOn": "2026-10-28",
                  "status": "Uncontrolled archive occurrences, not chronological event pairs",
                  "titles": titles}, indent=2))
