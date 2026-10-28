"""Read pinned Aselia HTML tables without confusing rowspans or navigation for records."""

import hashlib
import json
import re
import unicodedata
from pathlib import Path

from bs4 import BeautifulSoup


def text(cell):
    return " ".join(cell.get_text(" ", strip=True).split())


def lines(cell):
    return [" ".join(part.split()) for part in cell.get_text("\n", strip=True).splitlines()
            if part.strip()]


def name(cell):
    bold = cell.find("b")
    return text(bold) if bold else lines(cell)[0]


def slug(value):
    value = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")


def load_source(source, cache):
    payload = Path(cache, source["cache"]).read_bytes()
    if hashlib.sha256(payload).hexdigest() != source["sha256"]:
        raise ValueError("Cached response hash mismatch: " + source["title"])
    parsed = json.loads(payload)["parse"]
    if parsed["revid"] != source["revision"] or parsed["title"] != source["title"]:
        raise ValueError("Revision/title mismatch: " + source["title"])
    return parsed, BeautifulSoup(parsed["text"]["*"], "html.parser")


def tables(soup):
    for table in soup.select("table"):
        if table.find_parent("table"):
            continue
        first = table.find("tr").find_all(["th", "td"], recursive=False)
        if not first or first[0].name != "th":
            continue
        if any("navbox" in c for c in table.get("class", [])):
            continue
        headers = [text(c) for c in first]
        heading = table.find_previous(["h2", "h3", "h4"])
        section = text(heading).split("[")[0].strip() if heading else ""
        yield headers, section, table


def records(table, name_column):
    """Yield only newly introduced name cells, retaining separate explanatory rows."""
    spans = {}
    previous = None
    for row in table.find_all("tr"):
        cells = row.find_all(["th", "td"], recursive=False)
        if not cells or any(c.name == "th" for c in cells):
            continue
        expanded = {}
        inherited = set()
        for column, (cell, remaining) in list(spans.items()):
            expanded[column] = cell
            inherited.add(column)
            if remaining == 1:
                del spans[column]
            else:
                spans[column] = (cell, remaining - 1)
        column = 0
        explanation = len(cells) == 1 and int(cells[0].get("colspan", 1)) > 1
        for cell in cells:
            while column in expanded:
                column += 1
            for _ in range(int(cell.get("colspan", 1))):
                expanded[column] = cell
                rowspan = int(cell.get("rowspan", 1))
                if rowspan > 1:
                    spans[column] = (cell, rowspan - 1)
                column += 1
        if explanation:
            if previous is not None:
                previous["notes"].append(text(cells[0]))
            continue
        if name_column in inherited or name_column not in expanded:
            if previous is not None:
                previous["notes"].append("Source continuation (column order): " + "; ".join(text(cell) for cell in cells))
            continue
        if previous is not None:
            yield previous
        previous = {"cells": expanded, "notes": []}
    if previous is not None:
        yield previous


def write_json(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
