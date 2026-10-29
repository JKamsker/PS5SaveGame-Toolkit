"""Smoke-test static HTML, schema, search and assets over HTTP at both supported bases."""

import argparse
from pathlib import Path
from threading import Thread
from urllib.error import HTTPError
from urllib.request import urlopen

from serve_site import PROJECT_BASE, create_server

ROUTES = {
    "": b"Understand your saves.",
    "tutorials/first-plugin.html": b"ExampleGameDocument",
    "api/SaveToolkit.Sdk.IGamePlugin.html": b"IGamePlugin",
    "api/SaveToolkit.Sdk.ISaveDocument.html": b"ISaveDocument",
    "reference/http.html": b"http-reference.js",
    "reference/openapi.json": b'"openapi": "3.',
    "index.json": b"IGamePlugin",
    "public/main.css": b"docs-hero",
    "public/main.js": b"Skip to content",
    "public/docfx.min.js": None,
    "assets/http-reference.js": b"disableTelemetry: true",
    "assets/redoc/redoc.standalone.js": b"data:image/svg+xml;base64,",
    "assets/toolkit.svg": b"<svg",
}


def check(site):
    with create_server(site) as server:
        worker = Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            origin = f"http://127.0.0.1:{server.server_port}"
            for base in ("/", PROJECT_BASE + "/"):
                for path, expected in ROUTES.items():
                    with urlopen(origin + base + path, timeout=10) as response:
                        body = response.read()
                        if response.status != 200 or not body or (expected and expected not in body):
                            raise ValueError(f"Unexpected static response: {base}{path}")
                try:
                    urlopen(origin + base + "api/plugins", timeout=10).close()
                except HTTPError as error:
                    if error.code != 404:
                        raise
                else:
                    raise ValueError("Documentation preview must not host the application API.")
        finally:
            server.shutdown()
            worker.join(timeout=5)
    print(f"HTTP smoke: {len(ROUTES) * 2} static routes passed at root/project bases; API paths return 404.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site", required=True, type=Path)
    check(parser.parse_args().site)
