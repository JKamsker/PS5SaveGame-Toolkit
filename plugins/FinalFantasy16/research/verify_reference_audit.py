"""Check the 67-ID naming decision ledger against the generated licensed catalog.

python -I plugins/FinalFantasy16/research/verify_reference_audit.py
Metadata only; no downloads, game files or third-party code execution.
"""
import json
from pathlib import Path

PLUGIN = Path(__file__).resolve().parents[1]
PACK = PLUGIN / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.wiki.en"


def main():
    audit = json.loads((PLUGIN / "research/round2-naming-audit.json").read_bytes())
    records = json.loads((PACK / "items.json").read_bytes())["records"]
    names = {int(row["id"]): row for row in records}
    rows = audit["items"]
    if len(names) != len(records) or len(rows) != 67 or len({row["id"] for row in rows}) != 67:
        raise ValueError("Catalog or baseline audit has duplicate/missing IDs")
    resolved = 0
    for row in rows:
        record = names.get(row["id"])
        if row["status"] == "Named":
            resolved += 1
            if record is None or row["confidence"] != "Inferred" or record["attributes"]["confidence"] != "Inferred":
                raise ValueError(f"Missing record/evidence for {row['id']}")
            if any(row[key] != record[key] for key in ("name", "source")) or row["evidence"] != record["description"]:
                raise ValueError(f"Stale source/label for {row['id']}")
            if row["identifierSource"] != record["attributes"]["identifierSource"] or "limit" in record:
                raise ValueError(f"Stale identifier evidence/unproven cap for {row['id']}")
        elif row["status"] != "Unknown" or record is not None or row["confidence"] != "Unknown" or "name" in row or not row["reason"]:
            raise ValueError(f"Unreviewed mapping or missing rejection reason for {row['id']}")
    if resolved != audit["resolved"] or 67 - resolved != audit["stillUnknown"]:
        raise ValueError("Stale audit totals")
    corpus = json.loads((PLUGIN / "research/corpus-summary.json").read_bytes())["titles"][0]
    if corpus["namedMainIds"] != 105 + resolved or corpus["explicitUnknownMainIds"] != audit["stillUnknown"]:
        raise ValueError("Stale corpus name coverage")
    print(f"67 decisions verified: {resolved} licensed Inferred labels, {67-resolved} explicit Unknowns; no inferred caps")


if __name__ == "__main__":
    main()
