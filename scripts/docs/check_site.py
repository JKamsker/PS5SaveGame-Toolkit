"""Check generated links, anchors, assets and required references at root and project URLs."""

import argparse
from html.parser import HTMLParser
import json
from pathlib import Path
import re
from urllib.parse import unquote, urljoin, urlsplit

REQUIRED_SYMBOLS = (
    "SaveToolkit.Sdk.IGamePlugin", "SaveToolkit.Sdk.ISaveDocument",
    "SaveToolkit.Sdk.Views.ViewNode", "SaveToolkit.Sdk.Values.FieldSpec",
    "SaveToolkit.Sdk.BoundSaveDocument", "SaveToolkit.Sdk.Binary.BoundedBinaryCursor",
)
BASES = ("https://docs.invalid/", "https://docs.invalid/PS5SaveGame-Toolkit/")


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=True)
        self.anchors = set()
        self.links = []
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        for key in ("id", "name"):
            if attributes.get(key):
                self.anchors.add(attributes[key])
        for key in ("href", "src", "spec-url"):
            if attributes.get(key):
                self.links.append(attributes[key])


def local_target(site, source, link, base):
    parsed = urlsplit(link)
    if parsed.scheme or parsed.netloc:
        return None
    resolved = urlsplit(urljoin(base + source, link))
    prefix = urlsplit(base).path
    if not resolved.path.startswith(prefix):
        raise ValueError(f"URL escapes project base: {source}: {link}")
    relative = unquote(resolved.path[len(prefix):])
    target = (site / relative).resolve()
    if not target.is_relative_to(site.resolve()):
        raise ValueError(f"URL escapes output directory: {source}: {link}")
    if target.is_dir():
        target /= "index.html"
    return target, unquote(resolved.fragment)


def check(site):
    site = site.resolve()
    pages = {path: Page(path.read_text(encoding="utf-8")) for path in site.rglob("*.html")}
    if not pages:
        raise ValueError("The site contains no HTML pages.")
    errors = set()
    links = 0
    for path, page in pages.items():
        source = path.relative_to(site).as_posix()
        for link in page.links:
            if not link or link == "#":
                continue
            for base in BASES:
                try:
                    result = local_target(site, source, link, base)
                    if result is None:
                        continue
                    target, fragment = result
                    links += 1
                    if not target.is_file():
                        errors.add(f"{source}: missing target {link}")
                    elif fragment and target in pages and fragment not in pages[target].anchors:
                        errors.add(f"{source}: missing anchor {link}")
                except ValueError as error:
                    errors.add(str(error))
    # Check CSS dependencies as well as the script/style tags in HTML.
    for path in site.rglob("*.css"):
        for link in re.findall(r"url\(\s*['\"]?([^)'\"]+)", path.read_text(encoding="utf-8")):
            for base in BASES:
                result = local_target(site, path.relative_to(site).as_posix(), link.strip(), base)
                if result is not None and not result[0].is_file():
                    errors.add(f"{path.name}: missing CSS resource {link}")
    for symbol in REQUIRED_SYMBOLS:
        path = site / f"api/{symbol}.html"
        if path not in pages or symbol.rsplit(".", 1)[-1] not in path.read_text(encoding="utf-8"):
            errors.add(f"Missing rendered SDK symbol: {symbol}")
    required = ("index.html", "tutorials/first-plugin.html", "reference/http.html",
                "reference/openapi.json", "assets/redoc/redoc.standalone.js", "assets/http-reference.js",
                "assets/redoc/LICENSE.txt", "index.json", "xrefmap.yml", ".nojekyll")
    for name in required:
        if not (site / name).is_file():
            errors.add(f"Missing required output: {name}")
    http = (site / "reference/http.html").read_text(encoding="utf-8")
    initializer = (site / "assets/http-reference.js").read_text(encoding="utf-8")
    if 'id="http-reference"' not in http or 'disableTelemetry: true' not in initializer:
        errors.add("HTTP reference must render locally with telemetry disabled.")
    schema = json.loads((site / "reference/openapi.json").read_text(encoding="utf-8"))
    if not schema["openapi"].startswith("3.") or not schema["paths"]:
        errors.add("HTTP schema is missing its OpenAPI operations.")
    # A site must not accidentally contain app binaries, private data or source-stage inputs.
    for path in site.rglob("*"):
        if path.is_file() and (path.suffix.lower() in {".dll", ".pdb", ".zip", ".sav"} or
                               any(part in {"sdk-input", "schema-host", "artifacts", "workspaces", "packs"}
                                   for part in path.relative_to(site).parts)):
            errors.add(f"Unexpected published input: {path.relative_to(site)}")
    if errors:
        raise ValueError("\n".join(sorted(errors)))
    print(f"Site: {len(pages)} HTML pages; {links} local URL checks across root and project base; "
          f"{len(REQUIRED_SYMBOLS)} required SDK symbols; HTTP renderer, schema, search and assets verified.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", required=True, type=Path)
    check(parser.parse_args().site)
