"""Build factual identifier packs from reviewed, pinned downloads (never game saves).

Download inputs to a scratch directory; see ../REFERENCES.md. No downloaded code is run.
Usage: python -I generate.py INPUT_DIRECTORY
"""

import argparse
import gzip
import hashlib
import io
import json
import struct
import zlib
from pathlib import Path

REVISION = "c1fd00305e7db905a4e7de8cfa360d4f4efee3b2"
SOURCE = f"https://github.com/Deweh/CyberCAT-SimpleGUI/blob/{REVISION}"
ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks/cyberpunk2077.identifiers"
MAX_EXPANDED_BYTES = 128 * 1024 * 1024
EXPECTED_INPUTS = {
    "items.bin": "dbe6f314483984a1c0f5d034d76b54e68c387fb805f42ef4e3a5d8b3f050e252",
    "ItemClasses.json": "d63d00314f7a86e5a0464abc3773af56a292f6c59a61786e2c819a9fdcbae84c",
    "Facts.json": "08aeaeb811d0613bf9baef8f56fc33687455ce9278b6e0e760e903c70fa90ba6",
    "Vehicles.json": "f0991f27d919505282ce62941c9e8c57984e2fea1191cc06bee3c0e746518194",
    "FastTravel.json": "3830cd7deafcb61020ca8e27411b76e1b018093f031f30a4b657624bf9c0e1d7",
    "LICENSE": "a46ccc706fca37b7487bb8f3ac31fe93bd0dcf0572ca150c009d7f78bfc2e37e",
}


def tweak_hash(name):
    encoded = name.encode("utf-8")
    return zlib.crc32(encoded) | (len(encoded) << 32)


def fact_hash(name):
    value = 2166136261
    for byte in name.encode("utf-8"):
        value = ((value ^ byte) * 16777619) & 0xFFFFFFFF
    return value


def item_names(path, wanted):
    # Measured dictionary framing: expanded length, gzip, 10-byte header,
    # then .NET seven-bit-length UTF-8 strings. Only exact wanted hashes survive.
    raw = path.read_bytes()
    declared, = struct.unpack_from("<I", raw)
    if declared > MAX_EXPANDED_BYTES:
        raise ValueError("Dictionary exceeds expansion limit")
    with gzip.GzipFile(fileobj=io.BytesIO(raw[4:])) as stream:
        data = stream.read(MAX_EXPANDED_BYTES + 1)
    if len(data) != declared:
        raise ValueError("Dictionary expanded length mismatch")
    position = 10
    names = {}
    while position < len(data):
        length = 0
        for shift in range(0, 35, 7):
            byte = data[position]
            position += 1
            length |= (byte & 127) << shift
            if byte < 128:
                break
        else:
            raise ValueError("Invalid string length")
        end = position + length
        if end > len(data):
            raise ValueError("Truncated dictionary string")
        name = data[position:end].decode("utf-8")
        position = end
        if not name.startswith(("Items.", "Ammo.")):
            continue
        key = str(tweak_hash(name))
        if key not in wanted:
            continue
        if key in names and names[key] != name:
            raise ValueError(f"Ambiguous item identity: {key}")
        names[key] = name
    return names


def write_json(path, value):
    data = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", type=Path)
    args = parser.parse_args()
    for name, expected in EXPECTED_INPUTS.items():
        if hashlib.sha256((args.inputs / name).read_bytes()).hexdigest() != expected:
            raise ValueError(f"Source hash mismatch for {name}; audit revision/licence before updating")
    def unique_object(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate source key: {key}")
            result[key] = value
        return result

    def load(name):
        return json.loads((args.inputs / name).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    classes = load("ItemClasses.json")
    names = item_names(args.inputs / "items.bin", classes)
    # Retain the existing reviewed 36-entry policy dictionary without widening writes.
    baseline = json.loads((ROOT / "SaveToolkit.Plugins.Cyberpunk2077/Resources/ItemNames.json").read_text())
    for row in baseline:
        key = str(int(row["id"], 16))
        if key in names and names[key] != row["name"]:
            raise ValueError(f"Baseline identifier collision: {key}")
        names.setdefault(key, row["name"])
    items = [{"id": key, "name": name, "category": classes.get(key, {}).get("Type", "Unknown"),
              "source": f"{SOURCE}/CP2077SaveEditor/Resources/ItemClasses.json",
              "attributes": {"internalName": name, "coverage": "Identifier only; build/DLC availability unknown"}}
             for key, name in sorted(names.items(), key=lambda pair: pair[1])]
    facts = []
    for key, name in load("Facts.json").items():
        if fact_hash(name) != int(key):
            raise ValueError(f"Fact hash mismatch: {key}")
        facts.append({"id": key, "name": name, "category": "Raw fact identifier",
                      "attributes": {"canonicalName": name}})
    vehicles = [{"id": str(tweak_hash(name)), "name": name, "category": "Vehicle identifier"}
                for name in load("Vehicles.json")]
    travel = [{"id": str(tweak_hash(row["PointRecord"])), "name": row["PointRecord"],
               "category": "Phantom Liberty" if row["IsEP1"] else "Base game",
               "attributes": {"markerRef": row["MarkerRef"], "requiresEp1": str(row["IsEP1"]).lower()}}
              for row in load("FastTravel.json")]
    OUTPUT.mkdir(parents=True, exist_ok=True)
    datasets = []
    definitions = [("items", items, "items.bin", "Joined exact CRC32/UTF-8-length identities to ItemClasses.json; retained 36 reviewed baseline identifiers; no localization or game rules."),
                   ("facts", facts, "CP2077SaveEditor/Resources/Facts.json", "Preserved exact canonical strings, including whitespace; verified FNV-1a/32; added raw-identifier category."),
                   ("vehicles", vehicles, "CP2077SaveEditor/Resources/Vehicles.json", "Computed decimal-string CRC32/UTF-8-length identities; added identifier category; no ownership or availability claims."),
                   ("travel", travel, "CP2077SaveEditor/Resources/FastTravel.json", "Computed PointRecord decimal-string identities; retained MarkerRef and IsEP1 as attributes; no coordinates or unlock claims.")]
    for catalog, records, source, modifications in definitions:
        if len({row["id"] for row in records}) != len(records):
            raise ValueError(f"Duplicate or colliding {catalog} identities")
        digest = write_json(OUTPUT / f"{catalog}.json", {"records": records})
        datasets.append({"id": catalog, "kind": "records", "title": f"Cyberpunk {catalog} identifiers",
                         "files": [{"path": f"{catalog}.json", "sha256": digest}],
                         "attribution": {"sourceUrl": f"{SOURCE}/{source}", "author": "DeweySalt / Deweh and CyberCAT-SimpleGUI contributors",
                                         "license": "MIT", "licenseUrl": f"{SOURCE}/LICENSE", "retrieved": "2026-10-28",
                                         "modifications": modifications}})
        print(f"{catalog}: {len(records)} records, {digest}")
    write_json(OUTPUT / "pack.json", {"schema": 1, "id": "cyberpunk2077.identifiers", "version": "1.0.0",
               "title": "Cyberpunk 2077 community identifiers (not localization)",
               "description": "Pinned community identifiers; build/DLC completeness and gameplay semantics unknown. No descriptions, icons or legal quantity limits.",
               "plugins": ["cyberpunk2077"], "titleIds": ["PPSA04027"], "gameBuilds": [], "datasets": datasets})
    inputs = ["items.bin", "ItemClasses.json", "Facts.json", "Vehicles.json", "FastTravel.json", "LICENSE"]
    write_json(OUTPUT / "upstream.json", {"revision": REVISION, "retrieved": "2026-10-28",
               "files": [{"name": name, "sha256": hashlib.sha256((args.inputs / name).read_bytes()).hexdigest()} for name in inputs]})


if __name__ == "__main__":
    main()
