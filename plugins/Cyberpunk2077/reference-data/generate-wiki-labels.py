"""Offline deterministic CC BY-SA 3.0 base-name extraction; no downloads or game assets.

Run with python -I generate-wiki-labels.py <scratch-directory>. Supply MediaWiki API query
responses in wiki-pages.json, wiki-more.json and wiki-unity.json (rvprop=ids|content,
rvslots=main). Only the pinned pages below are used; other returned pages are ignored.
"""
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import quote
import zlib

PAGES = {
    "Bounce Back": (297811, "d070fa3115ac621407135aa6fa80a7b32295906f1d5c4534ee3081ebb0600628"),
    "F-GX Frag Grenade": (301113, "644d7ed2a437b1512316042b3f7f441d6e50141cbb61db8fe6b4e2077277415d"),
    "MaxDoc": (297812, "8ae3dcb6e2a14127b6370be15d7c60d82112c5eeeff0c90577d69d1d0998bd3c"),
    "EMP Grenade": (301118, "9b16e7bfa4a503f166ff3e93cc89e361e52497f036e3e5618522a736a5efec62"),
    "X-22 Flashbang Grenade": (287501, "2cc6649020f578474fe2eb2ce8c2c63d0217ca2c3e2074639b50a99a6d876559"),
    "Constitutional Arms Unity": (290048, "84d4155e2d6411de6d1f6c636f89ffbbe61a99aef648c8016ae39cbfaf0ebb3f"),
}


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    scratch = Path(sys.argv[1])
    plugin = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.Cyberpunk2077"
    output = plugin / "ReferencePacks" / "cyberpunk2077.wiki-labels.en"
    known = {r["id"] for r in json.loads((plugin / "ReferencePacks/cyberpunk2077.identifiers/items.json").read_text(encoding="utf-8"))["records"]}
    inputs = {}
    for filename in ["wiki-pages.json", "wiki-more.json", "wiki-unity.json"]:
        data = json.loads((scratch / filename).read_text(encoding="utf-8-sig"))
        for page in data["query"]["pages"].values():
            if page["title"] in PAGES:
                inputs[page["title"]] = page["revisions"][0]
    records = {}
    ledger = []
    for title, (revision, digest) in PAGES.items():
        source = inputs[title]
        content = source["slots"]["main"]["*"]
        if source["revid"] != revision or hashlib.sha256(content.encode()).hexdigest() != digest:
            raise ValueError(f"Pinned wiki revision/content mismatch: {title}")
        url = "https://cyberpunk.fandom.com/wiki/" + quote(title.replace(" ", "_")) + f"?oldid={revision}"
        ledger.append(dict(title=title, revision=revision, sourceUrl=url, wikitextSha256=digest))
        match = re.search(r"\|baseid\s*=\s*(.*?)(?=\n\||\n\}\})", content, re.S | re.I)
        if not match:
            raise ValueError(f"No explicit base IDs: {title}")
        for base in re.split(r"<br\s*/?>|\n", match[1], flags=re.I):
            base = base.strip()
            if not re.fullmatch(r"[A-Za-z0-9_]+", base):
                raise ValueError(f"Unexpected base ID markup: {base}")
            internal = "Items." + base
            raw = internal.encode()
            key = str(zlib.crc32(raw) | (len(raw) << 32))
            if key not in known:
                continue
            record = dict(id=key, name=title, source=url, attributes=dict(internalName=internal,
                scope="Wiki base-record name; instance tier, stats and availability unverified."))
            if key in records and records[key] != record:
                raise ValueError(f"Conflicting wiki labels for {internal}")
            records[key] = record
    output.mkdir(parents=True, exist_ok=True)
    write(output / "item-labels.json", dict(records=sorted(records.values(), key=lambda row: int(row["id"]))))
    content_hash = hashlib.sha256((output / "item-labels.json").read_bytes()).hexdigest()
    attribution = dict(sourceUrl="https://cyberpunk.fandom.com/wiki/Cyberpunk_Wiki", author="Cyberpunk Wiki contributors",
        license="CC-BY-SA-3.0", licenseUrl="https://creativecommons.org/licenses/by-sa/3.0/", retrieved="2026-10-28",
        modifications="Selected six page titles and explicit baseid/name joins only; verified hashes against the technical catalog. "
            "No prose, descriptions, limits, art or CDPR localization exported. Derived labels remain CC BY-SA 3.0; each source pins its revision.")
    write(output / "pack.json", dict(schema=1, id="cyberpunk2077.wiki-labels.en", version="1.0.0",
        title="Cyberpunk Wiki base-record names (English, CC BY-SA 3.0)", plugins=["cyberpunk2077"],
        titleIds=["PPSA04027"], gameBuilds=[], locale="en", datasets=[dict(id="item-labels", kind="records",
            title="Pinned wiki item names", files=[dict(path="item-labels.json", sha256=content_hash)], attribution=attribution)]))
    write(output / "upstream.json", dict(license="CC-BY-SA-3.0", pages=ledger,
        licensingSource="https://community.fandom.com/wiki/Help:Licensing?oldid=3983537",
        licensingWikitextSha256="37bb1449d363b79e2d4ac5cc0b41d50bdbc94057cf31ed569f118aa3d3c65558"))
    print(f"Generated {len(records)} pinned wiki base-record labels.")


if __name__ == "__main__":
    main()
