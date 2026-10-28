"""Rebuild the hash-confirmed identifier subset from local reference dumps.

Requires the supplied research toolkit's cryptography dependency. Reads save
entries in memory, without extracting or writing any save data. Reference code
must have been reviewed before use; this script never downloads or executes it.
"""

import argparse
import json
from pathlib import Path
import sys
import zipfile


def main():
    repo = Path(__file__).resolve().parents[3]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--rsz", required=True, type=Path)
    parser.add_argument("--strings", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1]
                        / "SaveToolkit.Plugins.Pragmata/Resources/HashNames.json")
    args = parser.parse_args()
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(repo / "docs/research/pragmata"))
    from pragmata_save import Reader, Save
    from hashes import murmur3

    observed = set()
    with zipfile.ZipFile(args.archive) as archive:
        for entry in archive.infolist():
            name = Path(entry.filename).name
            if not name.startswith("data") or not name.endswith(".bin"):
                continue
            reader = Reader(Save(archive.read(entry)).plaintext)
            reader.parse()
            for clazz in reader.classes:
                observed.add(int(clazz["class"], 16))
                observed.update(int(field["hash"], 16) for field in clazz["fields"])

    candidates = {}

    def consider(name):
        if len(name) > 2000:
            return
        hash_ = murmur3(name)
        if hash_ in observed:
            candidates.setdefault(hash_, set()).add(name)

    with args.rsz.open(encoding="utf-8") as source:
        for clazz in json.load(source).values():
            consider(clazz["name"])
            for field in clazz["fields"]:
                consider(field["name"])
    with args.strings.open(encoding="utf-8") as source:
        for name in source.read().splitlines():
            consider(name)
    with (repo / "docs/research/pragmata/verified_save_map.json").open(encoding="utf-8") as source:
        verified = json.load(source)
    for hash_, name in verified["field_name_hashes_confirmed"].items():
        if murmur3(name) != int(hash_, 16):
            raise ValueError(f"Research map name does not reproduce its hash: {name}")
        consider(name)

    ambiguous = {hash_: names for hash_, names in candidates.items() if len(names) != 1}
    if ambiguous:
        raise ValueError(f"Ambiguous hash matches need review: {ambiguous}")
    result = {f"{hash_:08x}": next(iter(names)) for hash_, names in sorted(candidates.items())}
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(result)} hash-confirmed names to {args.output}")


if __name__ == "__main__":
    main()
