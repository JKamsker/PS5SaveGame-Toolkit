"""Convert reviewed MIT Rust tables to F2 candidates; never download or execute upstream code.

Run: python -I import-progress-catalog.py <scratch directory containing the pinned files>.
All hashes cover original source bytes. No coordinates, art, quest edges or legal caps are imported.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re

REVISION = "014107f0ca1cff867b5f7565cb79d6684f9b35df"
ROOT = f"https://github.com/ClayAmore/ER-Save-Editor/blob/{REVISION}"
HASHES = {
    "graces.rs": "404ceccbad413c23ee5c2a79fa836006d276ffae355f2914aaf9034b71d470f5",
    "classes.rs": "9be056a27423f4745e86c1393c631c5b56d7a0c994b520ead06c8dce92702181",
    "event_flags.rs": "8d10136a6f079f9661333ca5488379de315f1cfe105f9279e9f590fabaca773b",
    "map_name.rs": "2eda78620723e68ec9769465224a2ccce01ec0bfab8c3e7bcc423e9799b46d24",
    "bosses.rs": "0e50d2ce32e1e19490e5834431560bee17d1dc3e2a173b6fae87b014635d687a",
    "maps.rs": "034450369b627cba24cb171fc565279d9cc9c6df81a7649b596870bf66a433a9",
    "bit.rs": "d198adad3fa0d6e71cadb29522bb038742c356c6c005fc0dc42b9dbaf762eb5d",
    "events.rs": "b88ae8439089a8407adedc1325a14b40ce57352d4853766131b57805b32d5e10",
    "LICENSE-MIT": "249b6ba67172fdf76ea07854e98a096383036d97d2e9fabed278fd47679a81fd",
}
OUTPUT = Path(__file__).resolve().parent.parent / "SaveToolkit.Plugins.EldenRing" / "ReferencePacks" / "eldenring.progress.en"
LITERAL = r'("(?:[^"\\]|\\.)*")'


def body(text):
    return text.split("HashMap::from([", 1)[1].split("]))", 1)[0]


def write_json(path, value):
    content = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    path.write_text(content, encoding="utf-8", newline="\n")
    return hashlib.sha256(content.encode()).hexdigest()


def attribution(files):
    return {
        "sourceUrl": f"{ROOT}/src/db",
        "author": "ClayAmore / ER-Save-Editor contributors",
        "license": "MIT", "licenseUrl": f"{ROOT}/LICENSE-MIT", "retrieved": "2026-10-28",
        "modifications": f"Parsed {files} as text; preserved names and source IDs; joined explicit event offsets/bits and region labels. "
                         "Added schema/provenance and PS5 uncertainty. No coordinates, artwork, quest prerequisites or legal caps.",
    }


def record(key, name, category, file, attributes):
    return {
        "id": key, "name": name, "category": category,
        "description": "Inferred source candidate only, not verified PS5 progress, discovery, completion or legal class minima. "
                       "Base-game pinned tables; no summoning-pool, DLC, NG+ or quest-state coverage.",
        "source": f"{ROOT}/src/db/{file}",
        "attributes": {"sourceRevision": REVISION, "sourceSha256": HASHES[file], "upstreamName": name, **attributes},
    }


def progress(texts):
    offsets = {}
    for event, offset, bit in re.findall(r'\((\d+),\(0x([0-9a-f]+),(\d)\)\)', texts["event_flags.rs"]):
        value = (int(offset, 16), int(bit))
        if event in offsets and offsets[event] != value:
            raise ValueError(f"Conflicting event lookup: {event}")
        offsets[event] = value
    regions = {key: json.loads(label) for key, label in re.findall(r'\(MapName::(\w+),\s*' + LITERAL + r'\)', body(texts["map_name.rs"]))}
    rows = []

    def add(kind, symbol, event, label, region, region_id, file):
        offset, bit = offsets[event]
        if not 0 <= offset < 0x1BF99F or not 0 <= bit < 8:
            raise ValueError(f"Invalid event range: {event}")
        rows.append(record(f"{kind}:{symbol}", json.loads(label), kind, file, {
            "schema": "eldenring-progress-candidate/1", "eventId": event,
            "offset": str(offset), "bit": str(bit), "region": region, "regionId": region_id,
            "lookupSource": f"{ROOT}/src/db/event_flags.rs", "lookupSha256": HASHES["event_flags.rs"],
        }))

    for symbol, region, event, label in re.findall(r'\(Grace::(\w+),\s*\(MapName::(\w+),\s*(\d+),\s*' + LITERAL + r'\)\)', body(texts["graces.rs"])):
        add("grace", symbol, event, label, regions[region], region, "graces.rs")
    # Boss regions are explicit upstream group comments; never join by nearest grace/name.
    region = None
    for line in body(texts["bosses.rs"]).splitlines():
        if line.strip().startswith("//"):
            region = line.strip()[2:].strip()
        match = re.search(r'\(Boss::(\w+),\s*\((\d+),\s*' + LITERAL + r'\)\)', line)
        if match:
            if not region:
                raise ValueError("Boss lacks explicit source region")
            symbol, event, label = match.groups()
            add("boss", symbol, event, label, region, re.sub(r'[^A-Za-z0-9]', '', region), "bosses.rs")
    for symbol, event, label in re.findall(r'\(Map::(\w+),\s*\((\d+),\s*' + LITERAL + r'\)\)', body(texts["maps.rs"])):
        add("map-control", symbol, event, label, "Map controls (not necessarily collectible fragments)", "MapControls", "maps.rs")
    if len(rows) != len({row["id"] for row in rows}):
        raise ValueError("Duplicate progress identities")
    return sorted(rows, key=lambda row: row["id"])


def classes(text):
    enum = text.split("pub enum ArcheType {", 1)[1].split("}", 1)[0]
    ids = {name: value for name, value in re.findall(r'(\w+)\s*=\s*(\d+)', enum)}
    rows = []
    for name, stats in re.findall(r'\(ArcheType::(\w+),\s*Stats\s*\{(.*?)\}\)', body(text), re.S):
        attributes = dict(re.findall(r'(\w+):\s*(\d+)', stats))
        rows.append(record(ids[name], name, "class", "classes.rs", {
            "schema": "eldenring-class-candidate/1", **attributes,
        }))
    if len(rows) != 10:
        raise ValueError("Incomplete class table")
    return sorted(rows, key=lambda row: int(row["id"]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_directory", type=Path)
    args = parser.parse_args()
    texts = {}
    for file, digest in HASHES.items():
        raw = (args.source_directory / file).read_bytes()
        if hashlib.sha256(raw).hexdigest() != digest:
            raise ValueError(f"Source differs from pinned revision: {file}")
        texts[file] = raw.decode("utf-8")
    datasets = [("eldenring-progress", "progress.json", progress(texts)),
                ("eldenring-classes", "classes.json", classes(texts["classes.rs"]))]
    OUTPUT.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schema": 1, "id": "eldenring.progress.en", "version": "1.0.0",
        "title": "Elden Ring: inferred base-game progress and classes (English)",
        "description": "Pinned MIT candidate lookups, not verified PS5 progress. No map coordinates/art or quest graph.",
        "plugins": ["eldenring"], "titleIds": [], "gameBuilds": [], "locale": "en", "datasets": [],
    }
    for dataset, filename, rows in datasets:
        digest = write_json(OUTPUT / filename, {"records": rows})
        manifest["datasets"].append({"id": dataset, "kind": "records", "title": dataset,
                                     "files": [{"path": filename, "sha256": digest}],
                                     "attribution": attribution("classes.rs" if dataset.endswith("classes") else "graces.rs, map_name.rs, bosses.rs, maps.rs and event_flags.rs")})
        print(f"{dataset}: {len(rows)} records; SHA-256 {digest}")
    write_json(OUTPUT / "pack.json", manifest)
    (OUTPUT / "LICENSE-MIT.txt").write_bytes((args.source_directory / "LICENSE-MIT").read_bytes())
    lines = ["# Progress and class candidate provenance", "", f"Pinned revision: `{REVISION}`; retrieved 2026-10-28.",
             "Author: ClayAmore / ER-Save-Editor contributors. MIT; full notice: LICENSE-MIT.txt.", "",
             "| Source file | Original SHA-256 |", "| --- | --- |"]
    for file, digest in HASHES.items():
        folder = "src/vm" if file == "events.rs" else "src/util" if file == "bit.rs" else "src/db"
        url = f"{ROOT}/{file}" if file == "LICENSE-MIT" else f"{ROOT}/{folder}/{file}"
        lines.append(f"| [{file}]({url}) | `{digest}` |")
    lines += ["", "Names/IDs unchanged in resources; upstreamName preserves exact text even where F2 trims display whitespace.",
              "Explicit table joins only; source boss group comments retained verbatim.",
              "Offsets/bits are the source's bounded sparse lookup, not eventId/8 or a universal block formula.",
              "These are Inferred read-only candidates on v150; no progress is certified from a set/unset bit.",
              "No art/coordinates, summoning pools, quest edges, DLC coverage or legal class minima are asserted.",
              "This pack contains no art/coordinates. The separate names pack supplies optional local map art and",
              "inferred placements; Map Genie remains an external link. Class starting values are reference facts,",
              "never a respec writer.",
              "Regenerate from the repository root with `python -I plugins/EldenRing/Evidence/import-progress-catalog.py <scratch>`."]
    (OUTPUT / "progress.source.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
