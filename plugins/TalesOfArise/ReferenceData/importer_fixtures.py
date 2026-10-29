"""Authored API-shaped inputs, not downloaded HTML or private game samples."""

import hashlib
import json
from html import escape


def table(headers, rows, section="Examples"):
    heading = "".join("<th>" + h + "</th>" for h in headers)
    body = "".join("<tr>" + "".join("<td>" + c + "</td>" for c in row) + "</tr>" for row in rows)
    return "<h2>" + section + "</h2><table><tr>" + heading + "</tr>" + body + "</table>"


def catalog_inputs():
    """Exercise every table dispatch branch and all eight datasets in source order."""
    examples = [
        ("ToA - Consumables", table(["Item Name", "Effect"], [
            ["<b>Apple Gel</b>", "Restore &amp; retain"], ["<b>Example Tonic</b>", "Unknown"]])),
        ("ToA - Materials", table(["Item Name", "Value"], [["<b>Éxample Ore</b>", "12"]])),
        ("ToA - Accessory Materials", table(["Item Name"], [["Example Mineral"]])),
        ("ToA - Valuables", table(["Item Name"], [["Example Key"]])),
        ("ToA - Fishing Items", table(["Rod", "Name"], [["symbol", "Example Rod"]])),
        ("ToA - Weapons (Swords)", table(["Name", "Attack"], [
            ["<b>Example Blade</b>", "10"], ["<b>Example Blade</b>", "20"]])),
        ("ToA - Armor (Capes)", table(["Name", "Defense"], [["Example Cape", "30"]])),
        ("ToA - Accessories", table(["Name"], [["Example Charm"]])),
        ("ToA - Alphen: Costumes", table(["Name"], [["Example Costume"]])),
        ("ToA - Shionne Imeris: Haircut, Style", table(["Name"], [["Example Style"]])),
        ("ToA - Attachments", table(["Name"], [["Example Attachment"]])),
        ("ToA - Alphen: Artes", table(["Arte Name", "Partner", "Requirements"], [
            ["Example Strike", "Shionne", "Level 4"], ["Example Strike", "Law", "Level 8"]],
            "Hidden Artes (base)")),
        ("ToA - Dohalim il Qaras: Artes", table(["Arte Name", "Requirements"], [
            ["Example Spell", "Unknown"]], "")),
        ("ToA - Skills", """<h2>Support</h2><table>
            <tr><th>Icon</th><th>Skill Name</th><th>Users</th><th colspan="2">Stacks</th><th>Effect</th></tr>
            <tr><th></th><th></th><th></th><th>Base</th><th>DLC</th><th></th></tr>
            <tr><td>symbol</td><td><b>Example Skill</b></td><td>Alphen<br>Law</td>
            <td>1<br>2</td><td>3<br>4</td><td>Published effect</td></tr></table>"""),
        ("ToA - Alphen: Titles", table(["Title", "Name", "Skills", "SP", "Bonus", "Requirements"], [[
            "symbol", "Example Panel", "<ul>" + "".join("<li>Node " + str(i) + "</li>" for i in range(5)) + "</ul>",
            "0<br>10<br>20<br>30<br>40", "Attack +5", "Story event"]])),
        ("ToA - Cooking", table(["Name", "Effect"], [["Example Recipe", "Effect"]])),
        ("ToA - Fishing", table(["Fish", "Name", "Region"], [["symbol", "Example Fish", "Pool"]])),
        ("ToA - Artifacts", table(["Artifact", "Name", "Effect"], [["symbol", "Example Artifact", "Unknown"]])),
        ("ToA - Boost Strikes", table(["Not imported"], [["Duplicated participation"]])),
        ("Template:ToA Characters", ""),
    ]
    result = []
    for index, (title, html) in enumerate(examples):
        wikitext = "<!-- {{Item|Unpublished guess}} -->"
        if title == "Template:ToA Characters":
            wikitext = """|playable=[[Alphen]] [[Shionne Vymer Imeris Daymore]] {{P|Dohalim il Qaras|Name}}
|allies=[[Alphen]] [[Example Ally|Example Alias]]
|media=[[Ignored]]
<noinclude>|other=[[Excluded]]</noinclude>"""
        elif title == "ToA - Consumables":
            wikitext += "{{Item|Apple Gel}}{{Item|Example Tonic}}"
        parsed = {"title": title, "revid": index + 100, "text": {"*": html +
                  '<style>ignored</style><sup class="reference">note</sup><span class="mw-editsection">edit</span>'},
                  "wikitext": {"*": wikitext}}
        payload = json.dumps({"parse": parsed}, ensure_ascii=False).encode("utf-8")
        source = {"title": title, "revision": parsed["revid"],
                  "url": "https://example.invalid/wiki/" + escape(title.replace(" ", "_")),
                  "cache": str(index) + ".json", "sha256": hashlib.sha256(payload).hexdigest()}
        result.append((source, payload))
    return result


def write_catalog_inputs(directory, inputs=None):
    sources = []
    for source, payload in catalog_inputs() if inputs is None else inputs:
        (directory / source["cache"]).write_bytes(payload)
        sources.append(source)
    path = directory / "sources.json"
    path.write_text(json.dumps(sources), encoding="utf-8")
    return path
