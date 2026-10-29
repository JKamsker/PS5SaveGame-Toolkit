"""Regenerate the embedded catalog from reviewed, pinned upstream text (never execute Rust).

Run with python -I and a scratch directory containing src_db_<table>_name.rs.
Downloads stay outside the repository. Source hashes deliberately reject other revisions.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re

REVISION = "014107f0ca1cff867b5f7565cb79d6684f9b35df"
SOURCE_ROOT = f"https://github.com/ClayAmore/ER-Save-Editor/blob/{REVISION}"
RETRIEVED = "2026-10-28"
LICENSE_HASH = "249b6ba67172fdf76ea07854e98a096383036d97d2e9fabed278fd47679a81fd"
TABLES = [
    ("weapon", "weapon", 0x00000000, "1809f87e82855c3bf5adb1b307af167c7ce0a74511473571574f305a4194bf7d"),
    ("item", "goods", 0x40000000, "f267fa9153e95128f9f2e35e224ee1b6d2b8564ee8ae0000d9c37488c14b795b"),
    ("armor", "armor", 0x10000000, "dbfbf4da769d6bd29b2ed22a86358e7d88b841a225bb10ce3e3174af959249df"),
    ("accessory", "talisman", 0x20000000, "c9cff96c0f2406c4403bf1fb4bed28393c1645ece1899d90da52e7ad7cab0d28"),
    ("aow", "ash", 0x80000000, "48dd5cd1f306f05be427cd30c9c8236b1df27ff96d31e025789f34fc59d92bbd"),
]
PAIR = re.compile(r'\(\s*(\d+)\s*,\s*("(?:[^"\\]|\\.)*")\s*\),?')
OUTPUT = Path(__file__).resolve().parent.parent / "SaveToolkit.Plugins.EldenRing" / "ReferencePacks" / "eldenring.names.en"


def write_json(path, value):
    content = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    path.write_text(content, encoding="utf-8", newline="\n")
    return hashlib.sha256(content.encode("utf-8")).hexdigest()


def convert(source_directory, table, category, prefix, expected_hash):
    raw = (source_directory / f"src_db_{table}_name.rs").read_bytes()
    if hashlib.sha256(raw).hexdigest() != expected_hash:
        raise ValueError(f"{table}: source differs from pinned revision {REVISION}")
    text = raw.decode("utf-8")
    body = text.split("HashMap::from([", 1)[1].split("]))", 1)[0]
    pairs = PAIR.findall(body)
    if PAIR.sub("", body).strip():
        raise ValueError(f"{table}: unparsed source content")
    records = []
    seen = set()
    source = f"{SOURCE_ROOT}/src/db/{table}_name.rs"
    for raw_id, literal in pairs:
        upstream_id = int(raw_id)
        if not 0 <= upstream_id <= 0xFFFFFFFF or upstream_id in seen:
            raise ValueError(f"{table}: invalid or duplicate id {upstream_id}")
        seen.add(upstream_id)
        name = json.loads(literal)
        # Upstream includes out-of-category sentinels (e.g. goods 999999999). Retain them
        # under a noncanonical key instead of manufacturing a real save-item identity.
        canonical = upstream_id < 0x10000000
        key = f"0x{prefix | upstream_id:08X}" if canonical else f"{category}:upstream:{upstream_id}"
        records.append({
            "id": key,
            "name": name or key,
            "category": category,
            "description": "Upstream identity label only; Inferred on PS5. Includes unused/test/unnamed rows. "
                           "No ownership, legal cap, upgrade compatibility or game-build coverage is asserted.",
            "source": source,
            "attributes": {
                "schema": "eldenring-canonical-full-id/1",
                "upstreamId": raw_id,
                "upstreamName": name,
                "nameStatus": "named" if name else "unnamed",
                "identityStatus": "canonical" if canonical else "upstream-sentinel",
                "sourceRevision": REVISION,
                "sourceSha256": expected_hash,
            },
        })
    records.sort(key=lambda record: record["id"])
    filename = f"{table}-names.json"
    digest = write_json(OUTPUT / filename, {"records": records})
    unnamed = sum(record["attributes"]["nameStatus"] == "unnamed" for record in records)
    sidecar = f"""# {category.title()} names and IDs

- Source: {source}
- Author/project: ClayAmore / ER-Save-Editor contributors.
- Licence: [MIT]({SOURCE_ROOT}/LICENSE-MIT); full notice in [LICENSE-MIT.txt](LICENSE-MIT.txt).
- Retrieved: {RETRIEVED}; pinned revision `{REVISION}`.
- Upstream SHA-256 (original bytes): `{expected_hash}`.
- Records: **{len(records)}**, including **{unnamed}** empty upstream names.
- Changes: parsed every literal ID/name pair without executing Rust; preserved all names verbatim,
  including test/unused entries; OR'ed representable IDs with category prefix `0x{prefix:08X}`; sorted by ID.
  Out-of-category upstream sentinels retain a noncanonical `category:upstream:decimal` key.
  Empty names display their raw catalog key, retaining the empty string in `upstreamName`.
  Added provenance, schema and uncertainty metadata. No descriptions, artwork or legal limits copied.
- Coverage: complete for this source table/revision, not every game version or DLC. Names alone
  never authorize item creation, quantity changes, equipment compatibility or upgrade writes.

This sidecar documents the MIT identity-label contribution to the `eldenring-items` F2 dataset.
The identity-only importer emits MIT attribution. A later visual-enrichment step adds selected
publisher text/icons and separate visuals.source.md provenance; those contributions do not become MIT.
The shared picker, Sources panel and catalog/inventory details expose attribution.
"""
    (OUTPUT / f"{table}-names.source.md").write_text(sidecar, encoding="utf-8", newline="\n")
    print(f"{table}: {len(records)} records; {unnamed} unnamed")
    return {"path": filename, "sha256": digest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_directory", type=Path)
    args = parser.parse_args()
    # Validate the entire input set before replacing any generated resource.
    license_bytes = (args.source_directory / "LICENSE-MIT").read_bytes()
    if hashlib.sha256(license_bytes).hexdigest() != LICENSE_HASH:
        raise ValueError("MIT notice differs from the pinned source")
    for table, _, _, expected_hash in TABLES:
        raw = (args.source_directory / f"src_db_{table}_name.rs").read_bytes()
        if hashlib.sha256(raw).hexdigest() != expected_hash:
            raise ValueError(f"{table}: source differs from pinned revision {REVISION}")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "LICENSE-MIT.txt").write_bytes(license_bytes)
    files = [convert(args.source_directory, *table) for table in TABLES]
    manifest = {
        "schema": 1, "id": "eldenring.names.en", "version": "2.0.0",
        "title": "Elden Ring: complete ClayAmore name tables (English)",
        "description": "All five pinned MIT item/weapon/armor/talisman/Ash of War name tables. "
                       "Identity-only; includes unused/test/unnamed rows; no verified limits or DLC/build guarantee.",
        "plugins": ["eldenring"], "titleIds": [], "gameBuilds": [], "locale": "en",
        "datasets": [{
            "id": "eldenring-items", "kind": "records", "title": "ClayAmore category-qualified name catalog",
            "files": files,
            "attribution": {
                "sourceUrl": f"https://github.com/ClayAmore/ER-Save-Editor/tree/{REVISION}/src/db",
                "author": "ClayAmore / ER-Save-Editor contributors",
                "license": "MIT", "licenseUrl": f"{SOURCE_ROOT}/LICENSE-MIT", "retrieved": RETRIEVED,
                "modifications": "Converted all literal pairs from weapon_name.rs, item_name.rs, armor_name.rs, "
                                 "accessory_name.rs and aow_name.rs to category-qualified IDs. Names unchanged; "
                                 "empty names display raw IDs and retain upstreamName. Added source hashes, "
                                 "schema and uncertainty metadata; no gameplay limits, prose or artwork.",
            },
        }],
    }
    write_json(OUTPUT / "pack.json", manifest)


if __name__ == "__main__":
    main()
